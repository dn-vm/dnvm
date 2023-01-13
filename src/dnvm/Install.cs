
using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde.Json;
using static System.Environment;

namespace Dnvm;

public sealed class Install
{
    private readonly GlobalOptions _globalConfig;
    // Place to install dnvm
    public readonly string InstallDir;
    private string SdkInstallDir => _globalConfig.SdkInstallDir;
    private string ManifestPath => _globalConfig.ManifestPath;

    private readonly Logger _logger;
    private readonly CommandArguments.InstallArguments _options;
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
        _globalConfig = options;
        _logger = logger;
        _options = args;
        if (_options.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        InstallDir = args.DnvmInstallPath ?? options.DnvmInstallPath;
        _feedUrl = _options.FeedUrl ?? GlobalOptions.DotnetFeedUrl;
        if (_feedUrl[^1] == '/')
        {
            _feedUrl = _feedUrl[..^1];
        }
    }

    public static Task<Result> Run(GlobalOptions options, Logger logger, CommandArguments.InstallArguments args)
    {
        return new Install(options, logger, args).Run();
    }

    public async Task<Result> Run()
    {
        _logger.Info("Install Directory: " + InstallDir);
        _logger.Info("SDK install directory: " + SdkInstallDir);
        try
        {
            Directory.CreateDirectory(InstallDir);
            Directory.CreateDirectory(SdkInstallDir);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error($"Cannot write to install location. Ensure you have appropriate permissions.");
            return Result.InstallLocationNotWritable;
        }

        if (_options.Self)
        {
            return await RunSelfInstall() == 0
                ? Result.Success
                : Result.SelfInstallFailed;
        }

        Manifest manifest;
        try
        {
            manifest = ManifestUtils.ReadOrCreateManifest(ManifestPath);
        }
        catch (InvalidDataException)
        {
            _logger.Error("Manifest file corrupted");
            return Result.ManifestFileCorrupted;
        }
        catch (Exception e)
        {
            _logger.Error("Error reading manifest file: " + e.Message);
            return Result.ManifestIOError;
        }

        if (manifest.TrackedChannels.Any(c => c.ChannelName == _options.Channel))
        {
            _logger.Log($"Channel '{_options.Channel}' is already being tracked." +
                " Did you mean to run 'dnvm update'?");
            return Result.ChannelAlreadyTracked;
        }

        manifest = manifest with {
            TrackedChannels = manifest.TrackedChannels.Add(new TrackedChannel {
                ChannelName = _options.Channel,
                InstalledSdkVersions = ImmutableArray<string>.Empty
            })
        };

        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await DotnetReleasesIndex.FetchLatestIndex(_feedUrl);
        }
        catch (Exception e)
        {
            _logger.Error("Could not fetch the releases index: ");
            _logger.Error(e.Message);
            return Result.CouldntFetchIndex;
        }

        RID rid = Utilities.CurrentRID;

        string? latestVersion = versionIndex.GetLatestReleaseForChannel(_options.Channel)?.LatestSdk;
        if (latestVersion is null)
        {
            _logger.Error("Could not fetch the latest package version");
            return Result.CouldntFetchLatestVersion;
        }
        _logger.Log("Found latest version: " + latestVersion);

        if (!_options.Force && manifest.InstalledSdkVersions.Contains(latestVersion))
        {
            _logger.Log($"Version {latestVersion} is already installed." +
                " Skipping installation. To install anyway, pass --force.");
            return 0;
        }

        var installResult = await InstallSdk(
            _logger,
            _options.Channel,
            latestVersion,
            rid,
            _feedUrl,
            manifest,
            ManifestPath,
            SdkInstallDir);

        if (installResult != Result.Success)
        {
            return installResult;
        }

        return 0;
    }

    public static async Task<Result> InstallSdk(
        Logger logger,
        Channel channel,
        string latestVersion,
        RID rid,
        string feedUrl,
        Manifest manifest,
        string manifestPath,
        string sdkInstallDir)
    {
        string archiveName = ConstructArchiveName(latestVersion, rid, Utilities.ZipSuffix);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        logger.Info("Archive path: " + archivePath);

        var link = ConstructDownloadLink(feedUrl, latestVersion, archiveName);
        logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        logger.Info("Existing manifest: " + result);

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough))
        using (var archiveHttpStream = await Program.HttpClient.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
            await tempArchiveFile.FlushAsync();
        }
        logger.Log($"Installing to {sdkInstallDir}");
        string? extractResult = await Utilities.ExtractArchiveToDir(archivePath, sdkInstallDir);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            logger.Error("Extract failed: " + extractResult);
            return Result.ExtractFailed;
        }

        logger.Info($"Adding installed version '{latestVersion}' to manifest.");
        manifest = manifest with { InstalledSdkVersions = manifest.InstalledSdkVersions.Add(latestVersion) };
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


    private async Task<int> LinuxAddToPath()
    {
        return await UnixAddToPathInShellFiles();
    }

    private async Task<int> MacAddToPath()
    {
        return await UnixAddToPathInShellFiles();
    }

    static string ConstructArchiveName(
        string? specificVersion,
        RID rid,
        string suffix)
    {
        return specificVersion is null
            ? $"dotnet-sdk-{rid}.{suffix}"
            : $"dotnet-sdk-{specificVersion}-{rid}.{suffix}";
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

    private async Task<int> RunSelfInstall()
    {
        if (!Utilities.IsSingleFile)
        {
            _logger.Log("Cannot self-install into target location: the current executable is not deployed as a single file.");
            return 1;
        }

        var procPath = Utilities.ProcessPath;
        _logger.Info("Location of running exe" + procPath);

        var targetPath = Path.Combine(InstallDir, Utilities.ExeName);
        if (!_options.Force && File.Exists(targetPath))
        {
            _logger.Log("dnvm is already installed at: " + targetPath);
            _logger.Log("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
        }
        else
        {
            try
            {
                _logger.Info($"Copying file from '{procPath}' to '{targetPath}'");
                File.Copy(procPath, targetPath, overwrite: _options.Force);
            }
            catch (Exception e)
            {
                _logger.Log($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
                return 1;
            }

        }

        // Set up path
        if (_options.UpdateUserEnvironment)
        {
            await AddToPath();
        }

        return 0;
    }

    private async Task<int> AddToPath()
    {
        if (Utilities.CurrentRID.OS == OSPlatform.Windows)
        {
            _logger.Log("Adding install directory to user path: " + InstallDir);
            WindowsAddToPath(InstallDir);
            _logger.Log("Adding SDK directory to user path: " + SdkInstallDir);
            WindowsAddToPath(SdkInstallDir)
        }
        else if (Utilities.CurrentRID.OS == OSPlatform.OSX)
        {
            int result = await MacAddToPath();
            if (result != 0)
            {
                _logger.Error("Failed to add to path");
            }
        }
        else
        {
            int result = await LinuxAddToPath();
            if (result != 0)
            {
                _logger.Error("Failed to add to path");
            }
        }
        return 0;
    }

    private void WindowsAddToPath(string pathToAdd)
    {
        var currentPathVar = _globalConfig.GetUserEnvVar("PATH");
        if (!(";" + currentPathVar + ";").Contains(pathToAdd))
        {
            _globalConfig.SetUserEnvVar("PATH", pathToAdd + ";" + currentPathVar);
        }
    }

    private async Task<int> UnixAddToPathInShellFiles()
    {
        _logger.Info("Setting environment variables in shell files");
        string resolvedEnvPath = Path.Combine(_globalConfig.DnvmHome, "env");
        // Using the full path to the install directory is usually fine, but on Unix systems
        // people often copy their dotfiles from one machine to another and fully resolved paths present a problem
        // there. Instead, we'll try to replace instances of the user's home directory with the $HOME
        // variable, which should be the most common case of machine-dependence.
        var portableEnvPath = resolvedEnvPath.Replace(_globalConfig.UserHome, "$HOME");
        string userShSuffix = $"""

if [ -f "{portableEnvPath}" ]; then
    . "{portableEnvPath}"
fi
""";
        FileStream? envFile;
        try
        {
            envFile = File.Open(resolvedEnvPath, FileMode.CreateNew);
        }
        catch
        {
            _logger.Info("env file already exists, skipping installation");
            envFile = null;
        }

        if (envFile is not null)
        {
            _logger.Info("Writing env sh file");
            using (envFile)
            using (var writer = new StreamWriter(envFile))
            {
                var newContent = GetEnvShContent()
                    .Replace("{install_loc}", _globalConfig.DnvmHome)
                    .Replace("{sdk_install_loc}", _globalConfig.SdkInstallDir);
                await writer.WriteAsync(newContent);
                await envFile.FlushAsync();
            }

            // Scan shell files for shell suffix and add it if it doesn't exist
            _logger.Log("Scanning for shell files to update");
            foreach (var shellFileName in ProfileShellFiles)
            {
                var shellPath = Path.Combine(_globalConfig.UserHome, shellFileName);
                _logger.Info("Checking for file: " + shellPath);
                if (File.Exists(shellPath))
                {
                    _logger.Log("Found " + shellPath);
                }
                else
                {
                    continue;
                }
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
        return 0;
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
