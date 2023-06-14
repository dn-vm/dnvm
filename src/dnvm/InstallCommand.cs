
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serde.Json;
using static Dnvm.Utilities;

namespace Dnvm;

public sealed class InstallCommand
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

    public InstallCommand(GlobalOptions options, Logger logger, CommandArguments.InstallArguments args)
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
        _sdkDir = args.SdkDir switch {
            {} sdkDir => new SdkDirName(sdkDir.ToLowerInvariant()),
            _ => GetSdkDirNameFromChannel(args.Channel)
        };
    }

    internal static SdkDirName GetSdkDirNameFromChannel(Channel channel)
    {
        return channel switch {
            Channel.Preview => new SdkDirName("preview"),
            _ => GlobalOptions.DefaultSdkDirName
        };
    }

    public static Task<Result> Run(GlobalOptions options, Logger logger, CommandArguments.InstallArguments args)
    {
        return new InstallCommand(options, logger, args).Run();
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

        return await InstallLatestFromChannel(
            _dnvmHome,
            _logger,
            _installArgs.Channel,
            _installArgs.Force,
            _feedUrl,
            ManifestPath,
            _sdkDir);
    }

    internal static async Task<Result> InstallLatestFromChannel(
        string dnvmHome,
        Logger logger,
        Channel channel,
        bool force,
        string feedUrl,
        string manifestPath,
        SdkDirName sdkDir)
    {
        string sdkInstallDir = Path.Combine(dnvmHome, sdkDir.Name);
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
        catch (Exception e) when (e is not OperationCanceledException)
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
        catch (Exception e) when (e is not OperationCanceledException)
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
            dnvmHome,
            logger,
            latestVersion,
            rid,
            feedUrl,
            manifest,
            sdkDir);

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
        string dnvmHome,
        Logger logger,
        string latestVersion,
        RID rid,
        string feedUrl,
        Manifest manifest,
        SdkDirName sdkDirName)
    {
        var sdkInstallPath = Path.Combine(dnvmHome, sdkDirName.Name);
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
                Utilities.ChmodExec(dotnetExePath);
            }
            catch (Exception e)
            {
                logger.Error("chmod failed: " + e.Message);
                return Result.ExtractFailed;
            }
        }
        CreateSymlinkIfMissing(dnvmHome, sdkDirName);

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

    /// <summary>
    /// Creates a symlink from the dotnet exe in the dnvm home directory to the dotnet exe in the
    /// sdk install directory.
    /// </summary>
    /// <remarks>
    /// Doesn't use a symlink on Windows because the dotnet muxer doesn't properly resolve through
    /// symlinks.
    /// </remarks>
    internal static void RetargetSymlink(string dnvmHome, SdkDirName sdkDirName)
    {
        var symlinkPath = Path.Combine(dnvmHome, DotnetSymlinkName);
        var sdkInstallDir = Path.Combine(dnvmHome, sdkDirName.Name);
        // Delete if it already exists
        try
        {
            File.Delete(symlinkPath);
        }
        catch { }
        if (OperatingSystem.IsWindows())
        {
            // On Windows, we can't create a symlink, so create a .cmd file that calls the dotnet.exe
            File.WriteAllText(symlinkPath, $"""
@echo off
"%~dp0{sdkDirName.Name}\{DotnetExeName}" %*
""");
        }
        else
        {
            // On Unix, we can create a symlink
            File.CreateSymbolicLink(symlinkPath, Path.Combine(sdkInstallDir, DotnetSymlinkName));
        }
    }

    private static void CreateSymlinkIfMissing(string dnvmHome, SdkDirName sdkDirName)
    {
        var symlinkPath = Path.Combine(dnvmHome, DotnetSymlinkName);
        if (!File.Exists(symlinkPath))
        {
            RetargetSymlink(dnvmHome, sdkDirName);
        }
    }


}
