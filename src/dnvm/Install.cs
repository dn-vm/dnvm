
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Serde.Json;
using static System.Environment;
using static Dnvm.Utilities;

namespace Dnvm;

public sealed class Install
{
    private readonly GlobalOptions _globalOptions;
    // Place to install dnvm
    private string _dnvmHome;
    private readonly SdkDirName _sdkDir;
    private string SdkInstallPath => Path.Combine(_dnvmHome, _sdkDir.Name);
    private string ManifestPath => _globalOptions.ManifestPath;

    private readonly Logger _logger;
    private readonly CommandArguments.InstallArguments _installArgs;
    private readonly string _feedUrl;

    public enum Result
    {
        Success = 0,
        CouldntFetchLatestVersion,
        InstallLocationNotWritable,
        NotASingleFile,
        ExtractFailed,
        SelfInstallFailed,
        ManifestIOError,
        ManifestFileCorrupted,
        ChannelAlreadyTracked,
        CouldntFetchIndex
    }

    public Install(GlobalOptions options, Logger logger, CommandArguments.InstallArguments args)
    {
        _globalOptions = options;
        _logger = logger;
        _installArgs = args;
        if (_installArgs.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        _dnvmHome = options.DnvmInstallPath;
        _feedUrl = _installArgs.FeedUrl ?? GlobalOptions.DotnetFeedUrl;
        if (_feedUrl[^1] == '/')
        {
            _feedUrl = _feedUrl[..^1];
        }
        // Use an explicit SdkDir if specified, otherwise, only the preview channel is isolated by
        // default.
        _sdkDir = (args.SdkDir, args.Channel) switch {
            ({} sdkDir, _) => new SdkDirName(sdkDir.ToLowerInvariant()),
            (_ , Channel.Preview) => new SdkDirName("preview"),
            _ => GlobalOptions.DefaultSdkDirName
        };
    }

    public static Task<Result> Run(GlobalOptions options, Logger logger, CommandArguments.InstallArguments args)
    {
        return new Install(options, logger, args).Run();
    }

    public async Task<Result> Run()
    {
        _logger.Info("Install Directory: " + _dnvmHome);
        _logger.Info("SDK install directory: " + SdkInstallPath);
        try
        {
            Directory.CreateDirectory(_dnvmHome);
            Directory.CreateDirectory(SdkInstallPath);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error($"Cannot write to install location. Ensure you have appropriate permissions.");
            return Result.InstallLocationNotWritable;
        }

        if (_installArgs.Self)
        {
            return await RunSelfInstall() == 0
                ? Result.Success
                : Result.SelfInstallFailed;
        }

        return await InstallLatestFromChannel(
            _logger,
            _installArgs.Channel,
            _installArgs.Force,
            _feedUrl,
            ManifestPath,
            _sdkDir,
            SdkInstallPath);
    }

    private static async Task<Result> InstallLatestFromChannel(
        Logger logger,
        Channel channel,
        bool force,
        string feedUrl,
        string manifestPath,
        SdkDirName sdkDir,
        string sdkInstallDir)
    {
        Manifest manifest;
        try
        {
            manifest = ManifestUtils.ReadOrCreateManifest(manifestPath);
        }
        catch (InvalidDataException)
        {
            logger.Error("Manifest file corrupted");
            return Result.ManifestFileCorrupted;
        }
        catch (Exception e)
        {
            logger.Error("Error reading manifest file: " + e.Message);
            return Result.ManifestIOError;
        }

        if (manifest.TrackedChannels.Any(c => c.ChannelName == channel))
        {
            logger.Log($"Channel '{channel}' is already being tracked." +
                " Did you mean to run 'dnvm update'?");
            return Result.ChannelAlreadyTracked;
        }

        manifest = manifest with {
            TrackedChannels = manifest.TrackedChannels.Add(new TrackedChannel {
                ChannelName = channel,
                SdkDirName = sdkDir,
                InstalledSdkVersions = ImmutableArray<string>.Empty
            })
        };

        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await DotnetReleasesIndex.FetchLatestIndex(feedUrl);
        }
        catch (Exception e)
        {
            logger.Error("Could not fetch the releases index: ");
            logger.Error(e.Message);
            return Result.CouldntFetchIndex;
        }

        RID rid = Utilities.CurrentRID;

        string? latestVersion = versionIndex.GetLatestReleaseForChannel(channel)?.LatestSdk;
        if (latestVersion is null)
        {
            logger.Error("Could not fetch the latest package version");
            return Result.CouldntFetchLatestVersion;
        }
        logger.Log("Found latest version: " + latestVersion);

        if (!force && manifest.InstalledSdkVersions.Any(s => s.Version == latestVersion))
        {
            logger.Log($"Version {latestVersion} is already installed." +
                " Skipping installation. To install anyway, pass --force.");
            return Result.Success;
        }

        var installResult = await InstallSdkVersionFromChannel(
            logger,
            latestVersion,
            rid,
            feedUrl,
            manifest,
            sdkInstallDir);

        if (installResult != Result.Success)
        {
            return installResult;
        }

        logger.Info($"Adding installed version '{latestVersion}' to manifest.");
        manifest = manifest with { InstalledSdkVersions = manifest.InstalledSdkVersions.Add(new InstalledSdk {
            Version = latestVersion,
            SdkDirName = sdkDir,
        }) };
        var oldTracked = manifest.TrackedChannels.First(t => t.ChannelName == channel);
        var newTracked = oldTracked with {
            InstalledSdkVersions = oldTracked.InstalledSdkVersions.Add(latestVersion)
        };
        manifest = manifest with { TrackedChannels = manifest.TrackedChannels.Replace(oldTracked, newTracked) };

        logger.Info("Writing manifest");
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, JsonSerializer.Serialize(manifest));
        File.Move(tmpFile, manifestPath, overwrite: true);

        logger.Log("Successfully installed");

        return Result.Success;
    }

    public static async Task<Result> InstallSdkVersionFromChannel(
        Logger logger,
        string latestVersion,
        RID rid,
        string feedUrl,
        Manifest manifest,
        string sdkInstallPath)
    {
        string archiveName = ConstructArchiveName(latestVersion, rid, Utilities.ZipSuffix);
        using var tempDir = new DirectoryResource(Directory.CreateTempSubdirectory().FullName);
        string archivePath = Path.Combine(tempDir.Path, archiveName);
        logger.Info("Archive path: " + archivePath);

        var link = ConstructDownloadLink(feedUrl, latestVersion, archiveName);
        logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        logger.Info("Existing manifest: " + result);

        logger.Log("Downloading dotnet SDK...");

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough))
        using (var archiveResponse = await Program.HttpClient.GetAsync(link))
        using (var archiveHttpStream = await archiveResponse.Content.ReadAsStreamAsync())
        {
            if (!archiveResponse.IsSuccessStatusCode)
            {
                logger.Error("Failed archive response");
                logger.Error(await archiveResponse.Content.ReadAsStringAsync());
            }
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
            await tempArchiveFile.FlushAsync();
        }
        logger.Log($"Installing to {sdkInstallPath}");
        string? extractResult = await Utilities.ExtractArchiveToDir(archivePath, sdkInstallPath);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            logger.Error("Extract failed: " + extractResult);
            return Result.ExtractFailed;
        }

        var dotnetExePath = Path.Combine(sdkInstallPath, Utilities.DotnetExeName);
        if (!OperatingSystem.IsWindows())
        {
            logger.Info("chmoding downloaded host");
            try
            {
                var mod = File.GetUnixFileMode(dotnetExePath);
                File.SetUnixFileMode(dotnetExePath, mod | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (Exception e)
            {
                logger.Error("chmod failed: " + e.Message);
                return Result.ExtractFailed;
            }
        }

        return Result.Success;
    }

    static string ConstructArchiveName(
        string? specificVersion,
        RID rid,
        string suffix)
    {
        return specificVersion is null
            ? $"dotnet-sdk-{rid}{suffix}"
            : $"dotnet-sdk-{specificVersion}-{rid}{suffix}";
    }

    static string ConstructDownloadLink(string feed, string latestVersion, string archiveName)
    {
        return $"{feed}/Sdk/{latestVersion}/{archiveName}";
    }

    private static string GetEnvShContent()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("dnvm.env.sh")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<Result> RunSelfInstall()
    {
        if (!Utilities.IsSingleFile)
        {
            _logger.Log("Cannot self-install into target location: the current executable is not deployed as a single file.");
            return Result.SelfInstallFailed;
        }

        if (_installArgs.Update)
        {
            _logger.Log("Running self-update install");
            return await SelfUpdate(_dnvmHome);
        }

        _logger.Log("Starting dnvm install");

        var procPath = Utilities.ProcessPath;
        _logger.Info("Location of running exe" + procPath);

        if (!_installArgs.Yes)
        {
            Console.Write($"Please select install location [default: {GlobalOptions.Default.DnvmInstallPath}]: ");
            var customInstallPath = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(customInstallPath))
            {
                _dnvmHome = customInstallPath;
            }
        }

        var targetPath = Path.Combine(_dnvmHome, Utilities.DnvmExeName);
        if (!_installArgs.Force && File.Exists(targetPath))
        {
            _logger.Log("dnvm is already installed at: " + targetPath);
            _logger.Log("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
            return Result.SelfInstallFailed;
        }

        var channel = _installArgs.Channel;
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
                    channel = channels[resultInt];
                    break;
                }
            }
        }

        var updateUserEnv = _installArgs.UpdateUserEnvironment;
        if (!_installArgs.Yes && MissingFromEnv())
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
            File.Copy(procPath, targetPath, overwrite: _installArgs.Force);
            _logger.Log("Dnvm installed successfully.");
        }
        catch (Exception e)
        {
            _logger.Log($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
            return Result.SelfInstallFailed;
        }

        var result = await InstallLatestFromChannel(
            _logger,
            channel,
            _installArgs.Force,
            _feedUrl,
            ManifestPath,
            _sdkDir,
            SdkInstallPath);

        if (result is not Result.Success)
        {
            return result;
        }

        RetargetSymlink(_dnvmHome, SdkInstallPath);

        // Set up path
        if (updateUserEnv)
        {
            await AddToPath();
        }

        return Result.Success;
    }

    /// <summary>
    /// Creates a symlink from the dotnet exe in the dnvm home directory to the dotnet exe in the
    /// sdk install directory.
    /// </summary>
    private static void RetargetSymlink(string dnvmHome, string sdkInstallDir)
    {
        var symlinkPath = Path.Combine(dnvmHome, DotnetExeName);
        // Delete if it already exists
        try
        {
            File.Delete(symlinkPath);
        }
        catch { }
        File.CreateSymbolicLink(symlinkPath, Path.Combine(sdkInstallDir, DotnetExeName));
    }

    /// <summary>
    /// Install the running binary to the specified location.
    /// </summary>
    internal async Task<Result> SelfUpdate(string dnvmHome)
    {
        var logger = _logger;
        if (!ReplaceBinary(dnvmHome, logger))
        {
            return Result.SelfInstallFailed;
        }
        logger.Info($"Retargeting symlink in {dnvmHome} to {SdkInstallPath}");
        RetargetSymlink(dnvmHome, SdkInstallPath);
        if (!OperatingSystem.IsWindows())
        {
            await WriteEnvFile(dnvmHome, SdkInstallPath, logger);
        }
        else
        {
            // Remove default SDK install path from PATH if present
            RemoveFromPath(SdkInstallPath);
        }

        return Result.Success;

        static bool ReplaceBinary(string dnvmHome, Logger logger)
        {
            var destPath = Path.Combine(dnvmHome, DnvmExeName);
            var srcPath = Utilities.ProcessPath;
            try
            {
                string backupPath = destPath + ".bak";
                logger.Info($"Swapping {destPath} with downloaded file at {srcPath}");
                File.Move(destPath, backupPath, overwrite: true);
                File.Move(srcPath, destPath, overwrite: false);
                logger.Log("Process successfully upgraded");
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

    private void RemoveFromPath(string pathVar)
    {
        var paths = GetEnvVar("PATH")?.Split(Path.PathSeparator);
        if (paths is not null)
        {
            var newPath = string.Join(Path.PathSeparator, paths.Where(p => p != pathVar));
            SetEnvVar("PATH", newPath);
        }
    }

    private bool MissingFromEnv()
    {
        if (GetEnvVar("DOTNET_ROOT") != SdkInstallPath ||
            !PathContains(_dnvmHome))
        {
            return true;
        }
        return false;
    }

    private bool PathContains(string path)
    {
        var pathVar = GetEnvVar("PATH");
        var sep = Path.PathSeparator;
        var matchVar = $"{sep}{pathVar}{sep}";
        return matchVar.Contains($"{sep}{path}{sep}");
    }

    private string? GetEnvVar(string varName)
    {
        if (OperatingSystem.IsWindows())
        {
            return _globalOptions.GetUserEnvVar(varName);
        }
        else
        {
            return GetEnvironmentVariable(varName);
        }
    }

    private void SetEnvVar(string varName, string value)
    {
        if (OperatingSystem.IsWindows())
        {
            _globalOptions.SetUserEnvVar(varName, value);
        }
        else
        {
            SetEnvironmentVariable(varName, value);
        }
    }

    private async Task<int> AddToPath()
    {
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
            _logger.Log("Adding install directory to user path: " + _dnvmHome);
            WindowsAddToPath(_dnvmHome);
            _logger.Log("Setting DOTNET_ROOT: " + SdkInstallPath);
            SetEnvironmentVariable("DOTNET_ROOT", SdkInstallPath, EnvironmentVariableTarget.User);

            _logger.Log("");
            _logger.Log("Finished setting environment variables. Please close and re-open your terminal.");
        }
        // Assume everything else is unix
        else
        {
            await WriteEnvFile(_dnvmHome, SdkInstallPath, _logger);
            await AddToShellFiles(_dnvmHome, _globalOptions.UserHome);
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
    private void WindowsAddToPath(string pathToAdd)
    {
        var currentPathVar = _globalOptions.GetUserEnvVar("PATH");
        if (!(";" + currentPathVar + ";").Contains(pathToAdd))
        {
            _globalOptions.SetUserEnvVar("PATH", pathToAdd + ";" + currentPathVar);
        }
    }

    private static async Task WriteEnvFile(string dnvmHome, string sdkInstallDir, Logger logger)
    {
        var envFilePath = Path.Combine(dnvmHome, "env");
        var newContent = GetEnvShContent()
            .Replace("{install_loc}", dnvmHome)
            .Replace("{sdk_install_loc}", sdkInstallDir);

        logger.Info("Writing env sh file");
        await File.WriteAllTextAsync(envFilePath, newContent);
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
