
using System;
using System.Collections;
using System.Collections.Generic;
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
    private readonly IEnumerable<string> _feedUrls;

    public enum Result
    {
        Success = 0,
        UnknownChannel,
        InstallLocationNotWritable,
        NotASingleFile,
        InstallFailed,
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
        _feedUrls = args.FeedUrl is not null
            ? [ args.FeedUrl.TrimEnd('/') ]
            : env.DotnetFeedUrls;
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
            _feedUrls,
            _sdkDir);
    }

    internal static async Task<Result> InstallLatestFromChannel(
        DnvmEnv dnvmEnv,
        Logger logger,
        Channel channel,
        bool force,
        IEnumerable<string> feedUrls,
        SdkDirName sdkDir)
    {
        Manifest manifest;
        try
        {
            manifest = await ManifestUtils.ReadOrCreateManifest(dnvmEnv);
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
            logger.Log($"Channel '{channel.GetDisplayName()}' is already being tracked." +
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
            versionIndex = await DotnetReleasesIndex.FetchLatestIndex(feedUrls);
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
            // If the channel index is missing there are two possibilities:
            //   1. The channel is not supported by the index
            //   2. The channel is supported but there are currently no builds available
            // We want to provide a helpful error message about the first case, but let the second
            // case go through without attempting to install anything.
            if (channel is Channel.Versioned)
            {
                logger.Error($"Could not find channel '{channel}' in the dotnet releases index.");
                return Result.UnknownChannel;
            }
            else
            {
                logger.Log($"""
No builds available for channel '{channel}'. This often happens for preview channels when the first
preview has not yet been released.
Proceeding without SDK installation.
""");
                dnvmEnv.WriteManifest(manifest);
                return Result.Success;
            }
        }

        // Proceed with SDK installation

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
            var installResult = await InstallCommand.InstallSdk(
                dnvmEnv,
                manifest,
                release,
                sdkDir,
                logger);

            if (installResult is not Result<Manifest, InstallCommand.InstallError>.Ok(var newManifest))
            {
                return Result.InstallFailed;
            }
            manifest = newManifest;
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
        dnvmEnv.WriteManifest(manifest);

        logger.Log("Successfully installed");

        return Result.Success;
    }
}
