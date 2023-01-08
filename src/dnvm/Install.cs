
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serde.Json;
using static System.Environment;

namespace Dnvm;

public sealed class Install
{
    // Place to install dnvm
    public readonly string InstallDir;
    // Place to install SDKs
    public readonly string SdkInstallDir;
    // Place to install or read manifest file.
    public readonly string ManifestPath;

    private readonly Logger _logger;
    private readonly Command.InstallOptions _options;

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

    public Install(Logger logger, Command.InstallOptions options)
    {
        _logger = logger;
        _options = options;
        if (_options.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        InstallDir = options.DnvmInstallPath ?? DefaultConfig.InstallDir;
        SdkInstallDir = options.SdkInstallPath ?? Path.Combine(InstallDir, "dotnet");
        ManifestPath = Path.Combine(InstallDir, ManifestUtils.FileName);
    }

    public async Task<Result> Handle()
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
            Console.Error.WriteLine("Manifest file corrupted");
            return Result.ManifestFileCorrupted;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Error reading manifest file: " + e.Message);
            return Result.ManifestIOError;
        }

        if (manifest.TrackedChannels.Any(c => c.ChannelName == _options.Channel))
        {
            Console.WriteLine($"Channel '{_options.Channel}' is already being tracked." +
                " Did you mean to run 'dnvm update'?");
            return Result.ChannelAlreadyTracked;
        }

        var feed = _options.FeedUrl;
        if (feed[^1] == '/')
        {
            feed = feed[..^1];
        }

        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await VersionInfoClient.FetchLatestIndex(feed);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Could not fetch the releases index: ");
            Console.Error.WriteLine(e.Message);
            return Result.CouldntFetchIndex;
        }

        RID rid = Utilities.CurrentRID;

        string? latestVersion = VersionInfoClient.GetLatestReleaseForChannel(versionIndex, _options.Channel)?.LatestSdk;
        if (latestVersion is null)
        {
            Console.Error.WriteLine("Could not fetch the latest package version");
            return Result.CouldntFetchLatestVersion;
        }
        _logger.Log("Found latest version: " + latestVersion);

        if (!_options.Force && manifest.InstalledVersions.Contains(latestVersion))
        {
            _logger.Log($"Version {latestVersion} is already installed." +
                " Skipping installation. To install anyway, pass --force.");
            return 0;
        }

        string archiveName = ConstructArchiveName(latestVersion, rid, Utilities.ZipSuffix);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        _logger.Info("Archive path: " + archivePath);

        var link = ConstructDownloadLink(feed, latestVersion, archiveName);
        _logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        _logger.Info("Existing manifest: " + result);

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough))
        using (var archiveHttpStream = await Program.HttpClient.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
            await tempArchiveFile.FlushAsync();
        }
        _logger.Log($"Installing to {SdkInstallDir}");
        string? extractResult = await Utilities.ExtractArchiveToDir(archivePath, SdkInstallDir);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            _logger.Error("Extract failed: " + extractResult);
            return Result.ExtractFailed;
        }

        if (_options.UpdateUserEnvironment)
        {
            await AddToPath();
        }

        _logger.Info($"Adding installed version '{latestVersion}' to manifest.");
        manifest = manifest with { InstalledVersions = manifest.InstalledVersions.Add(latestVersion) };
        var tracked = new TrackedChannel {
            ChannelName = _options.Channel,
            InstalledVersions = ImmutableArray.Create(latestVersion)
        };
        _logger.Info($"Adding channel {tracked} to manifest.");
        manifest = manifest with { TrackedChannels = manifest.TrackedChannels.Add(tracked) };

        _logger.Info("Writing manifest");
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, JsonSerializer.Serialize(manifest));
        File.Move(tmpFile, ManifestPath, overwrite: true);

        _logger.Log("Successfully installed");
        return 0;
    }


    private async Task<int> LinuxAddToPath(string pathToAdd)
    {
        return await UnixAddToPathInShellFiles(pathToAdd);
    }

    private async Task<int> MacAddToPath(string pathToAdd)
    {
        return await UnixAddToPathInShellFiles(pathToAdd);
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
            Console.WriteLine("Cannot self-install into target location: the current executable is not deployed as a single file.");
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
                Console.WriteLine($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
                return 1;
            }

        }

        // Set up path
        await AddToPath();

        return 0;
    }

    private async Task<int> AddToPath()
    {
        if (Utilities.CurrentRID.OS == OSPlatform.Windows)
        {
            Console.WriteLine("Adding install directory to user path: " + InstallDir);
            WindowsAddToPath(InstallDir);
        }
        else if (Utilities.CurrentRID.OS == OSPlatform.OSX)
        {
            int result = await MacAddToPath(InstallDir);
            if (result != 0)
            {
                _logger.Error("Failed to add to path");
            }
        }
        else
        {
            int result = await LinuxAddToPath(InstallDir);
            if (result != 0)
            {
                _logger.Error("Failed to add to path");
            }
        }
        return 0;
    }

    private static int WindowsAddToPath(string pathToAdd)
    {
        var currentPathVar = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        if (!(":" + currentPathVar + ":").Contains(pathToAdd))
        {
            Environment.SetEnvironmentVariable("PATH", pathToAdd + ":" + currentPathVar, EnvironmentVariableTarget.User);
        }
        return 0;
    }

    private async Task<int> UnixAddToPathInShellFiles(string pathToAdd)
    {
        _logger.Info("Setting environment variables in shell files");
        string resolvedEnvPath = Path.Combine(pathToAdd, "env");
        // Using the full path to the install directory is usually fine, but on Unix systems
        // people often copy their dotfiles from one machine to another and fully resolved paths present a problem
        // there. Instead, we'll try to replace instances of the user's home directory with the $HOME
        // variable, which should be the most common case of machine-dependence.
        var portableEnvPath = resolvedEnvPath.Replace(Environment.GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify), "$HOME");
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
                await writer.WriteAsync(GetEnvShContent().Replace("{install_loc}", pathToAdd));
                await envFile.FlushAsync();
            }

            // Scan shell files for shell suffix and add it if it doesn't exist
            _logger.Log("Scanning for shell files to update");
            foreach (var shellFileName in ProfileShellFiles)
            {
                var shellPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify), shellFileName);
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
                    if (!(await FileContainsLine(shellPath, $". \"{portableEnvPath}\"")))
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
