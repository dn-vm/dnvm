
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde.Json;
using Zio;
using static Dnvm.Utilities;

namespace Dnvm;

public sealed class TrackCommand
{
    private readonly DnvmEnv _env;
    // Place to install dnvm
    private readonly SdkDirName _sdkDir;

    private readonly Logger _logger;
    private readonly CommandArguments.TrackArguments _installArgs;
    private readonly string _feedUrl;

    public enum Result
    {
        Success = 0,
        UnknownChannel,
        InstallLocationNotWritable,
        NotASingleFile,
        ExtractFailed,
        SelfInstallFailed,
        ManifestIOError,
        ManifestFileCorrupted,
        ChannelAlreadyTracked,
        CouldntFetchIndex
    }

    public TrackCommand(DnvmEnv env, Logger logger, CommandArguments.TrackArguments args)
    {
        _env = env;
        _logger = logger;
        _installArgs = args;
        if (_installArgs.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        _feedUrl = _installArgs.FeedUrl ?? env.DotnetFeedUrl;
        if (_feedUrl[^1] == '/')
        {
            _feedUrl = _feedUrl[..^1];
        }
        // Use an explicit SdkDir if specified, otherwise, only the preview channel is isolated by
        // default.
        _sdkDir = args.SdkDir switch {
            {} sdkDir => new SdkDirName(sdkDir),
            _ => DnvmEnv.DefaultSdkDirName
        };
    }

    public static Task<Result> Run(DnvmEnv env, Logger logger, CommandArguments.TrackArguments args)
    {
        return new TrackCommand(env, logger, args).Run();
    }

    public async Task<Result> Run()
    {
        var dnvmHome = _env.RealPath(UPath.Root);
        var sdkInstallPath = Path.Combine(dnvmHome, _sdkDir.Name);
        _logger.Info("Install Directory: " + dnvmHome);
        _logger.Info("SDK install directory: " + sdkInstallPath);
        try
        {
            Directory.CreateDirectory(dnvmHome);
            Directory.CreateDirectory(sdkInstallPath);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error($"Cannot write to install location. Ensure you have appropriate permissions.");
            return Result.InstallLocationNotWritable;
        }

        return await InstallLatestFromChannel(
            _env,
            _logger,
            _installArgs.Channel,
            _installArgs.Force,
            _feedUrl,
            _sdkDir);
    }

    internal static async Task<Result> InstallLatestFromChannel(
        DnvmEnv dnvmFs,
        Logger logger,
        Channel channel,
        bool force,
        string feedUrl,
        SdkDirName sdkDir)
    {
        Manifest manifest;
        try
        {
            manifest = await ManifestUtils.ReadOrCreateManifest(dnvmFs);
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

        if (manifest.TrackedChannels().Any(c => c.ChannelName == channel))
        {
            logger.Log($"Channel '{channel}' is already being tracked." +
                " Did you mean to run 'dnvm update'?");
            return Result.ChannelAlreadyTracked;
        }

        manifest = manifest.TrackChannel(new RegisteredChannel {
            ChannelName = channel,
            SdkDirName = sdkDir,
            InstalledSdkVersions = EqArray<SemVersion>.Empty
        });

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

        var latestChannelIndex = versionIndex.GetChannelIndex(channel);
        if (latestChannelIndex is null)
        {
            logger.Error($"Could not channel '{channel}' in the dotnet releases index.");
            return Result.UnknownChannel;
        }
        var latestSdkVersion = SemVersion.Parse(latestChannelIndex.LatestSdk, SemVersionStyles.Strict);
        logger.Log("Found latest version: " + latestSdkVersion);

        var release = JsonSerializer.Deserialize<ChannelReleaseIndex>(
            await Program.HttpClient.GetStringAsync(latestChannelIndex.ChannelReleaseIndexUrl))
            .Releases.Single(r => r.Sdk.Version == latestSdkVersion);

        if (!force && manifest.InstalledSdks.Any(s => s.SdkVersion == latestSdkVersion && s.SdkDirName == sdkDir))
        {
            logger.Log($"Version {latestSdkVersion} is already installed in directory '{sdkDir.Name}'." +
                " Skipping installation. To install anyway, pass --force.");
        }
        else
        {
            var installResult = await InstallSdkVersionFromChannel(
                dnvmFs,
                logger,
                latestSdkVersion,
                rid,
                feedUrl,
                manifest,
                sdkDir);

            if (installResult != Result.Success)
            {
                return installResult;
            }

            manifest = manifest with { InstalledSdks = manifest.InstalledSdks.Add(new InstalledSdk {
                ReleaseVersion = release.ReleaseVersion,
                RuntimeVersion = release.Runtime.Version,
                AspNetVersion = release.AspNetCore.Version,
                SdkVersion = latestSdkVersion,
                SdkDirName = sdkDir,
            }) };
        }

        // Even if the SDK was already installed, we'll add it to the list of tracked versions
        // for this channel. Otherwise you end up with non-determinism where the order of channel
        // updates starts to matter. Consider if you track LTS and latest and the same version is in
        // both channels. One channel will "win" when installing it, and then the next will skip
        // installation. If the version is only added to one channel's tracking list, the result is
        // not deterministic. By adding it to both, it doesn't matter what order we update channels in.
        logger.Info($"Adding installed version '{latestSdkVersion}' to manifest.");
        var oldTracked = manifest.RegisteredChannels.Single(t => t.ChannelName == channel);
        if (oldTracked.InstalledSdkVersions.Contains(latestSdkVersion))
        {
            logger.Info("Version already tracked");
        }
        else
        {
            var newTracked = oldTracked with {
                InstalledSdkVersions = oldTracked.InstalledSdkVersions.Add(latestSdkVersion)
            };
            manifest = manifest with { RegisteredChannels = manifest.RegisteredChannels.Replace(oldTracked, newTracked) };
        }

        logger.Info("Writing manifest");
        dnvmFs.WriteManifest(manifest);

        logger.Log("Successfully installed");

        return Result.Success;
    }

    public static async Task<Result> InstallSdkVersionFromChannel(
        DnvmEnv dnvmFs,
        Logger logger,
        SemVersion latestVersion,
        RID rid,
        string feedUrl,
        Manifest manifest,
        SdkDirName sdkDirName)
    {
        var sdkInstallPath = UPath.Root / sdkDirName.Name;
        var latestVersionString = latestVersion.ToString();
        string archiveName = InstallCommand.ConstructArchiveName(latestVersionString, rid, Utilities.ZipSuffix);
        using var tempDir = new DirectoryResource(Directory.CreateTempSubdirectory().FullName);
        string archivePath = Path.Combine(tempDir.Path, archiveName);
        logger.Info("Archive path: " + archivePath);

        var link = ConstructDownloadLink(feedUrl, latestVersionString, archiveName);
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
        string? extractResult = await Utilities.ExtractArchiveToDir(archivePath, dnvmFs, sdkInstallPath);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            logger.Error("Extract failed: " + extractResult);
            return Result.ExtractFailed;
        }

        var dotnetExePath = sdkInstallPath / Utilities.DotnetExeName;
        if (!OperatingSystem.IsWindows())
        {
            logger.Info("chmoding downloaded host");
            try
            {
                Utilities.ChmodExec(dnvmFs.Vfs, dotnetExePath);
            }
            catch (Exception e)
            {
                logger.Error("chmod failed: " + e.Message);
                return Result.ExtractFailed;
            }
        }
        InstallCommand.CreateSymlinkIfMissing(dnvmFs, sdkDirName);

        return Result.Success;
    }

    static string ConstructDownloadLink(string feed, string latestVersion, string archiveName)
    {
        return $"{feed}/Sdk/{latestVersion}/{archiveName}";
    }
}
