
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Zio;
using Zio.FileSystems;
using static System.Environment;

namespace Dnvm;

public class SelfInstallCommand
{
    private readonly Logger _logger;
    private GlobalOptions _globalOptions;
    // Place to install dnvm
    private readonly CommandArguments.SelfInstallArguments _installArgs;
    private readonly string _feedUrl;

    public SelfInstallCommand(GlobalOptions options, Logger logger, CommandArguments.SelfInstallArguments args)
    {
        _globalOptions = options;
        _logger = logger;
        _installArgs = args;
        if (_installArgs.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        _feedUrl = _installArgs.FeedUrl ?? options.DotnetFeedUrl;
        if (_feedUrl[^1] == '/')
        {
            _feedUrl = _feedUrl[..^1];
        }
    }

    public static Task<Result> Run(GlobalOptions options, Logger logger, CommandArguments.SelfInstallArguments args)
    {
        return new SelfInstallCommand(options, logger, args).Run();
    }

    public enum Result
    {
        Success,
        SelfInstallFailed,
        InstallFailed,
    }

    public async Task<Result> Run()
    {
        var dnvmEnv = _globalOptions.DnvmEnv;
        using var physicalFs = new PhysicalFileSystem();
        if (!Utilities.IsSingleFile)
        {
            _logger.Log("Cannot self-install into target location: the current executable is not deployed as a single file.");
            return Result.SelfInstallFailed;
        }

        if (_installArgs.Update)
        {
            _logger.Log("Running self-update install");
            return await SelfUpdate(dnvmEnv);
        }

        _logger.Log("Starting dnvm install");

        var procPath = Utilities.ProcessPath;
        _logger.Info("Location of running exe" + procPath);

        if (!_installArgs.Yes)
        {
            Console.Write($"Please select install location [default: {GlobalOptions.DefaultDnvmHome}]: ");
            var customInstallPath = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(customInstallPath))
            {
                var newEnv = DnvmEnv.CreatePhysical(customInstallPath);
                var newOptions = new GlobalOptions(
                    _globalOptions.UserHome,
                    _globalOptions.DnvmHome,
                    newEnv,
                    _globalOptions.DotnetFeedUrl,
                    _globalOptions.DnvmReleasesUrl);
                _globalOptions.Dispose();
                _globalOptions = newOptions;
                dnvmEnv = newEnv;
            }
        }

        var targetPath = DnvmEnv.DnvmExePath;
        if (!_installArgs.Force && dnvmEnv.Vfs.FileExists(targetPath))
        {
            _logger.Log("dnvm is already installed at: " + targetPath);
            _logger.Log("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
            return Result.SelfInstallFailed;
        }

        var channel = Channel.Latest;
        if (!_installArgs.Yes)
        {
            Console.WriteLine("Which channel would you like to start tracking?");
            Console.WriteLine("Available channels: ");
            var channels = Enum.GetValues<Channel>();
            for (int i = 0; i < channels.Length; i++)
            {
                var c = channels[i];
                var name = Enum.GetName(c)!;
                var desc = c.GetDesc();
                Console.WriteLine($"\t{i + 1}) {name} - {desc}");
            }
            Console.WriteLine();
            while (true)
            {
                Console.Write($"Please select a channel [default: {channel}]: ");
                var resultStr = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(resultStr))
                {
                    break;
                }
                else if (int.TryParse(resultStr, out int resultInt) && resultInt > 0 && resultInt <= channels.Length)
                {
                    channel = channels[resultInt - 1];
                    break;
                }
            }
        }

        var updateUserEnv = _installArgs.UpdateUserEnvironment;
        var sdkDirName = InstallCommand.GetSdkDirNameFromChannel(channel);
        if (!_installArgs.Yes && MissingFromEnv(dnvmEnv, sdkDirName))
        {
            Console.Write("One or more paths are missing from the user environment. Attempt to update the user environment? [Y/n] ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() == "y")
            {
                updateUserEnv = true;
            }
        }

        _logger.Log("Proceeding with installation.");

        try
        {
            _logger.Info($"Copying file from '{procPath}' to '{targetPath}'");
            physicalFs.CopyFileCross(
                physicalFs.ConvertPathFromInternal(procPath),
                dnvmEnv.Vfs,
                targetPath,
                overwrite: _installArgs.Force);
            _logger.Log("Dnvm installed successfully.");
        }
        catch (Exception e)
        {
            _logger.Log($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
            return Result.SelfInstallFailed;
        }

        var result = await InstallCommand.InstallLatestFromChannel(
            dnvmEnv,
            _logger,
            channel,
            _installArgs.Force,
            _feedUrl,
            sdkDirName);

        if (result is not InstallCommand.Result.Success)
        {
            return Result.InstallFailed;
        }

        InstallCommand.RetargetSymlink(dnvmEnv, sdkDirName);

        // Set up path
        if (updateUserEnv)
        {
            await AddToPath(dnvmEnv, sdkDirName);
        }

        return Result.Success;
    }

    /// <summary>
    /// Install the running binary to the specified location.
    /// </summary>
    internal async Task<Result> SelfUpdate(DnvmEnv dnvmEnv)
    {
        SdkDirName sdkDirName;
        try
        {
            var manifest = dnvmEnv.ReadManifest();
            sdkDirName = manifest.CurrentSdkDir;
        }
        catch
        {
            sdkDirName = GlobalOptions.DefaultSdkDirName;
        }

        var dnvmHome = dnvmEnv.Vfs.ConvertPathToInternal(UPath.Root);
        var SdkInstallPath = Path.Combine(dnvmHome, sdkDirName.Name);
        var logger = _logger;
        if (!ReplaceBinary(dnvmHome, logger))
        {
            return Result.SelfInstallFailed;
        }
        logger.Info($"Retargeting symlink in {dnvmHome} to {SdkInstallPath}");
        InstallCommand.RetargetSymlink(dnvmHome, sdkDirName);
        if (!OperatingSystem.IsWindows())
        {
            await WriteEnvFile(dnvmEnv, SdkInstallPath, logger);
        }
        else
        {
            // Remove default SDK install path from PATH if present
            RemoveFromPath(dnvmEnv, SdkInstallPath);
        }

        return Result.Success;

        static bool ReplaceBinary(string dnvmHome, Logger logger)
        {
            var destPath = Path.Combine(dnvmHome, Utilities.DnvmExeName);
            var srcPath = Utilities.ProcessPath;
            try
            {
                string backupPath = destPath + ".bak";
                logger.Info($"Swapping {destPath} with downloaded file at {srcPath}");
                File.Move(destPath, backupPath, overwrite: true);
                File.Move(srcPath, destPath, overwrite: false);
                logger.Log("Process successfully upgraded");
                // Set last write time
                File.SetLastWriteTimeUtc(destPath, DateTime.UtcNow);
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    // Can't delete the open file on Windows
                    File.Delete(backupPath);
                }
                return true;
            }
            catch (Exception e)
            {
                logger.Error("Couldn't replace existing binary: " + e.Message);
                return false;
            }
        }
    }

    private static Task WriteEnvFile(DnvmEnv dnvmFs, string sdkInstallDir, Logger logger)
    {
        var newContent = GetEnvShContent()
            .Replace("{install_loc}", dnvmFs.Vfs.ConvertPathToInternal(UPath.Root))
            .Replace("{sdk_install_loc}", sdkInstallDir);

        logger.Info("Writing env sh file");
        dnvmFs.Vfs.WriteAllText(DnvmEnv.EnvPath, newContent);
        return Task.CompletedTask;
    }

    private static string GetEnvShContent()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("dnvm.env.sh")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }


    private bool PathContains(DnvmEnv dnvmEnv, string path)
    {
        var pathVar = GetEnvVar(dnvmEnv, "PATH");
        var sep = Path.PathSeparator;
        var matchVar = $"{sep}{pathVar}{sep}";
        return matchVar.Contains($"{sep}{path}{sep}");
    }

    private string? GetEnvVar(DnvmEnv dnvmEnv, string varName)
    {
        if (OperatingSystem.IsWindows())
        {
            return dnvmEnv.GetUserEnvVar(varName);
        }
        else
        {
            return GetEnvironmentVariable(varName);
        }
    }

    private void SetEnvVar(DnvmEnv dnvmEnv, string varName, string value)
    {
        if (OperatingSystem.IsWindows())
        {
            dnvmEnv.SetUserEnvVar(varName, value);
        }
        else
        {
            SetEnvironmentVariable(varName, value);
        }
    }


    private void RemoveFromPath(DnvmEnv dnvmEnv, string pathVar)
    {
        var paths = GetEnvVar(dnvmEnv, "PATH")?.Split(Path.PathSeparator);
        if (paths is not null)
        {
            var newPath = string.Join(Path.PathSeparator, paths.Where(p => p != pathVar));
            SetEnvVar(dnvmEnv, "PATH", newPath);
        }
    }

    private bool MissingFromEnv(DnvmEnv dnvmEnv, SdkDirName sdkDirName)
    {
        var dnvmHome = dnvmEnv.Vfs.ConvertPathToInternal(UPath.Root);
        string SdkInstallPath = Path.Combine(dnvmHome, sdkDirName.Name);
        if (GetEnvVar(dnvmEnv, "DOTNET_ROOT") != SdkInstallPath ||
            !PathContains(dnvmEnv, dnvmHome))
        {
            return true;
        }
        return false;
    }

    private async Task<int> AddToPath(DnvmEnv dnvmEnv, SdkDirName sdkDir)
    {
        string SdkInstallPath = Path.Combine(dnvmEnv.RealPath(UPath.Root), sdkDir.Name);
        var dnvmHome = dnvmEnv.Vfs.ConvertPathToInternal(UPath.Root);
        if (OperatingSystem.IsWindows())
        {
            if (FindDotnetInSystemPath())
            {
                // dotnet.exe is in one of the system path variables. Produce a warning
                // that the dnvm dotnet.exe will not appear on the path as long as the
                // system variable is present.
                _logger.Log("");
                _logger.Warn("Found 'dotnet.exe' inside the System PATH environment variable. " +
                    "System PATH is always preferred over user path on Windows, so the dnvm-installed " +
                    "dotnet.exe will not be accessible until it is removed. " +
                    "It is strongly recommended to remove dotnet from your System PATH now.");
                _logger.Log("");
            }
            _logger.Log("Adding install directory to user path: " + dnvmHome);
            WindowsAddToPath(dnvmEnv, dnvmHome);
            _logger.Log("Setting DOTNET_ROOT: " + SdkInstallPath);
            SetEnvironmentVariable("DOTNET_ROOT", SdkInstallPath, EnvironmentVariableTarget.User);

            _logger.Log("");
            _logger.Log("Finished setting environment variables. Please close and re-open your terminal.");
        }
        // Assume everything else is unix
        else
        {
            await WriteEnvFile(dnvmEnv, SdkInstallPath, _logger);
            await AddToShellFiles(dnvmHome, _globalOptions.UserHome);
        }
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private bool FindDotnetInSystemPath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        if (pathVar is null)
        {
            return false;
        }
        var paths = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            if (File.Exists(Path.Combine(p, "dotnet.exe")))
            {
                return true;
            }
        }
        return false;
    }

    [SupportedOSPlatform("windows")]
    private void WindowsAddToPath(DnvmEnv dnvmEnv, string pathToAdd)
    {
        var currentPathVar = dnvmEnv.GetUserEnvVar("PATH");
        if (!(";" + currentPathVar + ";").Contains(pathToAdd))
        {
            dnvmEnv.SetUserEnvVar("PATH", pathToAdd + ";" + currentPathVar);
        }
    }

    private async Task AddToShellFiles(string dnvmHome, string userHome)
    {
        _logger.Info("Setting environment variables in shell files");

        string resolvedEnvPath = Path.Combine(dnvmHome, "env");
        // Using the full path to the install directory is usually fine, but on Unix systems
        // people often copy their dotfiles from one machine to another and fully resolved paths present a problem
        // there. Instead, we'll try to replace instances of the user's home directory with the $HOME
        // variable, which should be the most common case of machine-dependence.
        var portableEnvPath = resolvedEnvPath.Replace(userHome, "$HOME");
        string userShSuffix = $"""

if [ -f "{portableEnvPath}" ]; then
    . "{portableEnvPath}"
fi
""";
        // Scan shell files for shell suffix and add it if it doesn't exist
        _logger.Log("Scanning for shell files to update");
        var filesToUpdate = new List<string>();
        foreach (var shellFileName in ProfileShellFiles)
        {
            var shellPath = Path.Combine(_globalOptions.UserHome, shellFileName);
            _logger.Info("Checking for file: " + shellPath);
            if (File.Exists(shellPath))
            {
                _logger.Log("Found " + shellPath);
                filesToUpdate.Add(shellPath);
            }
            else
            {
                continue;
            }
        }

        foreach (var shellPath in filesToUpdate)
        {
            try
            {
                if (!await FileContainsLine(shellPath, $". \"{portableEnvPath}\""))
                {
                    _logger.Log("Adding env import to: " + shellPath);
                    await File.AppendAllTextAsync(shellPath, userShSuffix);
                }
            }
            catch (Exception e)
            {
                // Ignore if the file can't be accessed
                _logger.Info($"Couldn't write to file {shellPath}: {e.Message}");
            }
        }
    }

    private static ImmutableArray<string> ProfileShellFiles => ImmutableArray.Create<string>(
        ".profile",
        ".bashrc",
        ".zshrc"
    );


    private static async Task<bool> FileContainsLine(string filePath, string contents)
    {
        using var file = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open);
        var stream = file.CreateViewStream();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.Contains(contents))
            {
                return true;
            }
        }
        return false;
    }
}
