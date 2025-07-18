
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Spectre.Console;
using Zio;
using Zio.FileSystems;
using static System.Environment;

namespace Dnvm;

public class SelfInstallCommand
{
    public sealed record Options
    {
        public bool Verbose { get; init; } = false;
        public bool Force { get; init; } = false;
        /// <summary>
        /// URL to the dotnet feed containing the releases index and download artifacts.
        /// </summary>
        public string? FeedUrl { get; init; }
        /// <summary>
        /// Answer yes to every question or use the defaults.
        /// </summary>
        public bool Yes { get; init; } = false;
        /// <summary>
        /// Indicates that this is an update to an existing dnvm installation.
        /// </summary>
        public bool Update { get; init; } = false;
        /// <summary>
        /// Path to overwrite.
        /// </summary>
        public string? DestPath { get; init; } = null;
    }

    private readonly DnvmEnv _env;
    private readonly Logger _logger;
    // Place to install dnvm
    private readonly Options _opts;

    public SelfInstallCommand(DnvmEnv env, Logger logger, Options opts)
    {
        _env = env;
        _logger = logger;
        _opts = opts;
    }

    public static Task<Result> Run(DnvmEnv env, Logger logger, DnvmSubCommand.SelfInstallArgs args)
    {
        return Run(env, logger, new Options
        {
            Verbose = args.Verbose ?? false,
            Force = args.Force ?? false,
            FeedUrl = args.FeedUrl,
            Yes = args.Yes ?? false,
            Update = args.Update ?? false,
            DestPath = args.DestPath
        });
    }

    public static async Task<Result> Run(DnvmEnv env, Logger logger, Options opt)
    {
        var console = env.Console;

        if (opt.Verbose)
        {
            logger.Enabled = true;
        }

        if (!Utilities.IsSingleFile)
        {
            console.Error("Cannot self-install into target location: the current executable is not deployed as a single file.");
            return Result.SelfInstallFailed;
        }

        if (opt.Update is true)
        {
            logger.Log("Running self-update install");
            return await SelfUpdate(opt.DestPath, logger, env);
        }

        console.WriteLine("Starting dnvm install");

        if (!opt.Yes)
        {
            var dnvmHome = Environment.GetEnvironmentVariable("DNVM_HOME") ?? DnvmEnv.DefaultDnvmHome;
            console.WriteLine("The dnvm binary, manifest, and all SDKs will be installed under the dnvm home directory:");
            console.WriteLine();
            console.WriteLine($"	{dnvmHome}");
            console.WriteLine();
            console.WriteLine("You can change this location by setting the DNVM_HOME environment variable.");
        }

        var targetPath = opt.DestPath is not null
            ? env.DnvmHomeFs.ConvertPathFromInternal(opt.DestPath)
            : DnvmEnv.DnvmExePath;
        if (!opt.Force && env.DnvmHomeFs.FileExists(targetPath))
        {
            console.Error("dnvm is already installed at: " + targetPath);
            console.WriteLine("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
            return Result.SelfInstallFailed;
        }

        Channel channel = new Channel.Latest();
        if (!opt.Yes)
        {
            console.WriteLine("Which channel would you like to start tracking?");
            console.WriteLine("Available channels:");
            List<Channel> channels = [new Channel.Latest(), new Channel.Sts(), new Channel.Lts(), new Channel.Preview()];
            for (int i = 0; i < channels.Count; i++)
            {
                var c = channels[i];
                var name = c.GetDisplayName();
                var desc = c.GetDesc();
                console.WriteLine($"\t{i + 1}) {name} - {desc}");
            }
            console.WriteLine();
            while (true)
            {
                console.WriteLine($"Please select a channel [default: {channel.GetDisplayName()}]:");
                console.Write("> ");
                var resultStr = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(resultStr))
                {
                    break;
                }
                else if (int.TryParse(resultStr, out int resultInt) && resultInt > 0 && resultInt <= channels.Count)
                {
                    channel = channels[resultInt - 1];
                    break;
                }
            }
        }

        var updateUserEnv = true;
        var sdkDirName = DnvmEnv.DefaultSdkDirName;
        if (!opt.Yes && MissingFromEnv(env, sdkDirName))
        {
            console.WriteLine("One or more paths are missing from the user environment. Attempt to update the user environment?");
            console.Write("[Y/n]> ");
            updateUserEnv = Console.ReadLine()?.Trim().ToLowerInvariant() is not "n";
        }

        console.WriteLine("Proceeding with installation.");

        return await new SelfInstallCommand(env, logger, opt).Run(env.RealPath(targetPath), channel, sdkDirName, updateUserEnv);
    }

    public enum Result
    {
        Success,
        SelfInstallFailed,
        InstallFailed,
    }

    public async Task<Result> Run(string targetPath, Channel channel, SdkDirName newDirName, bool updateUserEnv)
    {
        var procPath = Utilities.ProcessPath;
        _logger.Log("Location of running exe: " + procPath);

        try
        {
            using var physicalFs = new PhysicalFileSystem();
            _logger.Log($"Copying file from '{procPath}' to '{targetPath}'");
            physicalFs.CopyFile(
                physicalFs.ConvertPathFromInternal(procPath),
                physicalFs.ConvertPathFromInternal(targetPath),
                overwrite: _opts.Force);
            if (!OperatingSystem.IsWindows())
            {
                Utilities.ChmodExec(physicalFs, targetPath);
            }
            _env.Console.WriteLine("Dnvm installed successfully.");
        }
        catch (Exception e)
        {
            _env.Console.Error($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
            return Result.SelfInstallFailed;
        }

        var result = await TrackCommand.Run(_env, _logger, new TrackCommand.Options
        {
            Channel = channel,
            Force = _opts.Force,
            Verbose = _opts.Verbose,
            FeedUrl = _opts.FeedUrl,
            SdkDir = newDirName,
            Yes = _opts.Yes
        });

        if (result is not (TrackCommand.Result.Success or TrackCommand.Result.ChannelAlreadyTracked))
        {
            _logger.Log("Track failed: " + result);
            return Result.InstallFailed;
        }

        SdkDirName oldDirName;
        try
        {
            var manifest = await Manifest.ReadManifestUnsafe(_env);
            oldDirName = manifest.CurrentSdkDir;
        }
        catch
        {
            oldDirName = newDirName;
        }
        SelectCommand.SelectDir(_logger, _env, oldDirName, newDirName);

        // Set up path
        if (updateUserEnv)
        {
            await UpdateEnv(_logger, _env, newDirName);
        }

        return Result.Success;
    }

    /// <summary>
    /// Install the running binary to the specified location.
    /// </summary>
    internal static async Task<Result> SelfUpdate(string? destPath, Logger logger, DnvmEnv dnvmEnv)
    {
        if (destPath is null)
        {
            throw new ArgumentNullException(nameof(destPath), "Destination path must be specified for self-update.");
        }
        logger.Log($"Installing to {destPath}");

        SdkDirName sdkDirName;
        try
        {
            var manifest = await Manifest.ReadManifestUnsafe(dnvmEnv);
            sdkDirName = manifest.CurrentSdkDir;
        }
        catch
        {
            sdkDirName = DnvmEnv.DefaultSdkDirName;
        }

        if (!ReplaceBinary(destPath, dnvmEnv.Console, logger))
        {
            return Result.SelfInstallFailed;
        }

        // Select the SDK directory if not already selected
        SelectCommand.SelectDir(logger, dnvmEnv, sdkDirName, sdkDirName);

        logger.Log("Updating environment");
        await UpdateEnv(logger, dnvmEnv, sdkDirName);

        logger.Log($"selfinstall completed successfully");
        return Result.Success;

        static bool ReplaceBinary(string destPath, IAnsiConsole console, Logger logger)
        {
            var srcPath = Utilities.ProcessPath;
            try
            {
                string backupPath = destPath + ".bak";
                logger.Log($"Swapping {destPath} with downloaded file at {srcPath}");
                File.Move(destPath, backupPath, overwrite: true);
                File.Move(srcPath, destPath, overwrite: false);
                console.WriteLine("Process successfully upgraded");
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
                console.Error("Couldn't replace existing binary: " + e.Message);
                return false;
            }
        }
    }

    private static bool PathContains(DnvmEnv dnvmEnv, string path)
    {
        var pathVar = GetEnvVar(dnvmEnv, "PATH");
        var sep = Path.PathSeparator;
        var matchVar = $"{sep}{pathVar}{sep}";
        return matchVar.Contains($"{sep}{path}{sep}");
    }

    private static string? GetEnvVar(DnvmEnv dnvmEnv, string varName)
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

    private static void SetEnvVar(DnvmEnv dnvmEnv, string varName, string value)
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


    private static void RemoveFromPath(DnvmEnv dnvmEnv, string pathVar)
    {
        var paths = GetEnvVar(dnvmEnv, "PATH")?.Split(Path.PathSeparator);
        if (paths is not null)
        {
            var newPath = string.Join(Path.PathSeparator, paths.Where(p => p != pathVar));
            SetEnvVar(dnvmEnv, "PATH", newPath);
        }
    }

    private static bool MissingFromEnv(DnvmEnv dnvmEnv, SdkDirName sdkDirName)
    {
        var dnvmHome = dnvmEnv.RealPath(UPath.Root);
        string SdkInstallPath = Path.Combine(dnvmHome, sdkDirName.Name);
        if (GetEnvVar(dnvmEnv, "DOTNET_ROOT") != SdkInstallPath ||
            !PathContains(dnvmEnv, dnvmHome))
        {
            return true;
        }
        return false;
    }

    private static async Task<int> UpdateEnv(Logger logger, DnvmEnv env, SdkDirName sdkDir)
    {
        var console = env.Console;
        var dnvmHome = env.RealPath(UPath.Root);
        var sdkInstallDir = env.RealPath(DnvmEnv.GetSdkPath(sdkDir));
        if (OperatingSystem.IsWindows())
        {
            logger.Log("Setting environment variables in Windows");
            if (FindDotnetInSystemPath())
            {
                // dotnet.exe is in one of the system path variables. Produce a warning
                // that the dnvm dotnet.exe will not appear on the path as long as the
                // system variable is present.
                console.Warn("Found 'dotnet.exe' inside the System PATH environment variable. " +
                    "System PATH is always preferred over User path on Windows, so the dnvm-installed " +
                    "dotnet.exe will not be accessible until it is removed. " +
                    "It is strongly recommended to remove dotnet from your System PATH now.");
            }
            console.WriteLine("Adding install directory to user path: " + dnvmHome);
            WindowsAddToPath(env, dnvmHome);
            console.WriteLine("Setting DOTNET_ROOT: " + sdkInstallDir);
            env.SetUserEnvVar("DOTNET_ROOT", sdkInstallDir);
            if (dnvmHome != DnvmEnv.DefaultDnvmHome)
            {
                console.WriteLine("Setting DNVM_HOME: " + dnvmHome);
                env.SetUserEnvVar("DNVM_HOME", dnvmHome);
            }

            console.WriteLine("");
            console.WriteLine("Finished setting environment variables. Please close and re-open your terminal.");
            logger.Log("Completed updating environment variables.");
        }
        // Assume everything else is unix
        else
        {
            WriteEnvShFile(logger, dnvmHome, env, sdkInstallDir);
            await AddToShellFiles(console, logger, dnvmHome, env.UserHome);
        }
        return 0;

        static void WriteEnvShFile(Logger logger, string dnvmHome, DnvmEnv env, string sdkInstallDir)
        {
            var newContent = Resources.GetEnvShContent()
                .Replace("{install_loc}", dnvmHome)
                .Replace("{sdk_install_loc}", sdkInstallDir);

            logger.Log("Writing env sh file");
            env.DnvmHomeFs.WriteAllText(DnvmEnv.EnvPath, newContent);

        }
        static async Task AddToShellFiles(IAnsiConsole console, Logger logger, string dnvmHome, string userHome)
        {
            logger.Log("Setting environment variables in shell files");

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

            // If DNVM_HOME is non-default, add it to the env.sh file
            if (dnvmHome != DnvmEnv.DefaultDnvmHome)
            {
                userShSuffix += Environment.NewLine +
                    $"export DNVM_HOME=\"{dnvmHome}\"" + Environment.NewLine;
            }
            // Scan shell files for shell suffix and add it if it doesn't exist
            console.WriteLine("Scanning for shell files to update");
            var filesToUpdate = new List<string>();
            foreach (var shellFileName in ProfileShellFiles)
            {
                var shellPath = Path.Combine(userHome, shellFileName);
                logger.Log("Checking for file: " + shellPath);
                if (File.Exists(shellPath))
                {
                    console.WriteLine("Found " + shellPath);
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
                        console.WriteLine("Adding env import to: " + shellPath);
                        await File.AppendAllTextAsync(shellPath, userShSuffix);
                    }
                }
                catch (Exception e)
                {
                    // Ignore if the file can't be accessed
                    logger.Log($"Couldn't write to file {shellPath}: {e.Message}");
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool FindDotnetInSystemPath()
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
    private static void WindowsAddToPath(DnvmEnv dnvmEnv, string pathToAdd)
    {
        var console = dnvmEnv.Console;

        console.WriteLine("Checking for path to add: " + pathToAdd);
        var currentPathVar = dnvmEnv.GetUserEnvVar("PATH");
        if (!$";{currentPathVar};".Contains($";{pathToAdd};"))
        {
            console.WriteLine("Adding to PATH: " + pathToAdd);
            dnvmEnv.SetUserEnvVar("PATH", pathToAdd + ";" + currentPathVar);
        }
        console.WriteLine("PATH: " + dnvmEnv.GetUserEnvVar("PATH"));
        console.WriteLine("PATH updated.");
    }

    private static ImmutableArray<string> ProfileShellFiles => [".profile", ".bashrc", ".zshrc" ];

    private static async Task<bool> FileContainsLine(string filePath, string contents)
    {
        using var file = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(file);
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
