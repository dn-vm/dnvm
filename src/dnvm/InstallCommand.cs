
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using Spectre.Console;
using StaticCs;
using Zio;
using Zio.FileSystems;

namespace Dnvm;

public static partial class InstallCommand
{
    public enum Result
    {
        Success = 0,
        CouldntFetchReleaseIndex,
        UnknownChannel,
        ManifestFileCorrupted,
        ManifestIOError,
        InstallError
    }

    public sealed record Options
    {
        public required SemVersion SdkVersion { get; init; }
        public bool Force { get; init; } = false;
        public SdkDirName? SdkDir { get; init; } = null;
        public bool Verbose { get; init; } = false;
        public (UPath Dir, IFileSystem DirFs)? TargetDir { get; init; } = null;
    }

    public static async Task<Result> Run(DnvmEnv env, Logger logger, DnvmSubCommand.InstallArgs args)
    {
        (UPath Dir, IFileSystem DirFs)? targetDir = null;
        if (args.Dir is not null)
        {
            // Convert relative paths to absolute paths based on current working directory
            UPath dirPath = args.Dir;
            if (!dirPath.IsAbsolute)
            {
                dirPath = env.Cwd / dirPath;
            }
            targetDir = (dirPath, env.CwdFs);
        }

        Result result = await Run(env, logger, new Options
        {
            SdkVersion = args.SdkVersion,
            Force = args.Force ?? false,
            SdkDir = args.SdkDir,
            Verbose = args.Verbose ?? false,
            TargetDir = targetDir,
        });

        if (result is not Result.Success && args.SdkVersion.Patch < 100)
        {
            env.Console.Error($"Requested SDK version '{args.SdkVersion}' with patch '{args.SdkVersion.Patch}'. " +
            "The dotnet SDK typically has 3 digit patches versions. " +
            $"Did you mean '{args.SdkVersion.WithSuggestedThreeDigitPatch()}'?");
        }
        return result;
    }

    public static async Task<Result> Run(DnvmEnv env, Logger logger, Options options)
    {
        var console = env.Console;
        var sdkDir = options.SdkDir ?? DnvmEnv.DefaultSdkDirName;

        if (options.TargetDir is ({ } targetDir, { } targetFs))
        {
            return await InstallToDir(
                targetDir,
                targetFs,
                env,
                options.SdkVersion,
                logger);
        }

        using var @lock = await ManifestLock.Acquire(env);
        Manifest manifest;
        try
        {
            manifest = await @lock.ReadOrCreateManifest(env);
        }
        catch (InvalidDataException)
        {
            console.Error("Manifest file corrupted");
            return Result.ManifestFileCorrupted;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            console.Error("Error reading manifest file: " + e.Message);
            return Result.ManifestIOError;
        }

        var sdkVersion = options.SdkVersion;
        var channel = new Channel.VersionedMajorMinor(sdkVersion.Major, sdkVersion.Minor);

        if (!options.Force && manifest.IsSdkInstalled(sdkVersion, sdkDir))
        {
            console.WriteLine($"Version {sdkVersion} is already installed in directory '{sdkDir.Name}'." +
                " Skipping installation. To install anyway, pass --force.");
            return Result.Success;
        }

        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, env.DotnetFeedUrls);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            console.Error($"Could not fetch the releases index: {e.Message}");
            return Result.CouldntFetchReleaseIndex;
        }

        var result = await TryGetReleaseFromIndex(env.HttpClient, versionIndex, channel, sdkVersion)
            ?? await TryGetReleaseFromServer(env, sdkVersion);
        if (result is not ({ } sdkComponent, { } release))
        {
            console.Error($"SDK version '{sdkVersion}' could not be found in .NET releases index or server.");
            return Result.UnknownChannel;
        }

        var installError = await InstallSdk(@lock, env, manifest, sdkComponent, release, sdkDir, logger);
        if (installError is not Result<Manifest, InstallError>.Ok)
        {
            return Result.InstallError;
        }

        return Result.Success;
    }

    private static async Task<Result> InstallToDir(
        UPath dir,
        IFileSystem targetFs,
        DnvmEnv env,
        SemVersion sdkVersion,
        Logger logger
    )
    {
        var console = env.Console;
        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, env.DotnetFeedUrls);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            console.Error($"Could not fetch the releases index: {e.Message}");
            return Result.CouldntFetchReleaseIndex;
        }

        var channel = new Channel.VersionedMajorMinor(sdkVersion.Major, sdkVersion.Minor);
        var result = await TryGetReleaseFromIndex(env.HttpClient, versionIndex, channel, sdkVersion)
            ?? await TryGetReleaseFromServer(env, sdkVersion);
        if (result is not ({ } sdkComponent, { } release))
        {
            console.Error($"SDK version '{sdkVersion}' could not be found in .NET releases index or server.");
            return Result.UnknownChannel;
        }

        var ridString = Utilities.CurrentRID.ToString();
        var downloadFile = sdkComponent.Files.Single(f => f.Rid == ridString && f.Url.EndsWith(Utilities.ZipSuffix));
        var link = downloadFile.Url;
        logger.Log("Download link: " + link);

        env.Console.WriteLine($"Downloading SDK {sdkVersion} for {ridString}");
        var err = await InstallSdkToDir(
            curMuxerVersion: null,
            release.Runtime.Version,
            env.HttpClient,
            env.Console,
            link,
            targetFs,
            dir,
            env.TempFs,
            logger);
        if (err is not null)
        {
            return Result.InstallError;
        }
        return Result.Success;
    }

    internal static async Task<(ChannelReleaseIndex.Component, ChannelReleaseIndex.Release)?> TryGetReleaseFromIndex(
        ScopedHttpClient httpClient,
        DotnetReleasesIndex versionIndex,
        Channel channel,
        SemVersion sdkVersion)
    {
        if (versionIndex.GetChannelIndex(channel) is { } channelIndex)
        {
            var channelReleaseIndexText = await httpClient.GetStringAsync(channelIndex.ChannelReleaseIndexUrl);
            var releaseIndex = JsonSerializer.Deserialize<ChannelReleaseIndex>(channelReleaseIndexText);
            var result =
                from r in releaseIndex.Releases
                from sdk in r.Sdks
                where sdk.Version == sdkVersion
                select (sdk, r);

            if (result.Any())
            {
                return result.First();
            }
        }

        return null;
    }


    internal static async Task<(ChannelReleaseIndex.Component, ChannelReleaseIndex.Release)?> TryGetReleaseFromServer(
        DnvmEnv env,
        SemVersion sdkVersion)
    {
        foreach (var feedUrl in env.DotnetFeedUrls)
        {
            var downloadUrl = $"{feedUrl.TrimEnd('/')}/Sdk/{sdkVersion}/productCommit-{Utilities.CurrentRID}.json";
            try
            {
                var productCommitData = JsonSerializer.Deserialize<CommitData>(await env.HttpClient.GetStringAsync(downloadUrl));
                if (productCommitData.Sdk.Version != sdkVersion)
                {
                    throw new InvalidOperationException("Fetched product commit data does not match requested SDK version");
                }
                var archiveName = ConstructArchiveName(versionString: sdkVersion.ToString(), Utilities.CurrentRID, Utilities.ZipSuffix);
                var sdk = new ChannelReleaseIndex.Component
                {
                    Version = sdkVersion,
                    Files = [ new ChannelReleaseIndex.File
                    {
                        Name = archiveName,
                        Url = MakeSdkUrl(feedUrl, sdkVersion, archiveName),
                        Rid = Utilities.CurrentRID.ToString(),
                    }]
                };

                var release = new ChannelReleaseIndex.Release
                {
                    Sdk = sdk,
                    AspNetCore = new ChannelReleaseIndex.Component
                    {
                        Version = productCommitData.Aspnetcore.Version,
                        Files = []
                    },
                    Runtime = new()
                    {
                        Version = productCommitData.Runtime.Version,
                        Files = []
                    },
                    ReleaseVersion = productCommitData.Runtime.Version,
                    Sdks = [sdk],
                    WindowsDesktop = new()
                    {
                        Version = productCommitData.Windowsdesktop.Version,
                        Files = []
                    },
                };
                return (sdk, release);
            }
            catch
            {
                // Swallow exception
            }
        }
        return null;
    }

    private static string MakeSdkUrl(string feedUrl, SemVersion version, string archiveName)
    {
        return feedUrl.TrimEnd('/') + $"/Sdk/{version}/{archiveName}";
    }

    [GenerateDeserialize]
    private partial record CommitData
    {
        public required Component Sdk { get; init; }
        public required Component Aspnetcore { get; init; }
        public required Component Runtime { get; init; }
        public required Component Windowsdesktop { get; init; }

        [GenerateDeserialize]
        public partial record Component
        {
            [SerdeMemberOptions(DeserializeProxy = typeof(SemVersionProxy))]
            public required SemVersion Version { get; init; }
        }
    }

    [Closed]
    internal enum InstallError
    {
        DownloadFailed,
        ExtractFailed
    }

    /// <summary>
    /// Install the given SDK inside the given directory, and update the manifest. Does not update the channel manifest.
    /// </summary>
    /// <exception cref="InvalidOperationException">Throws when manifest already contains the given SDK.</exception>
    internal static async Task<Result<Manifest, InstallError>> InstallSdk(
        ManifestLock @lock,
        DnvmEnv env,
        Manifest manifest,
        ChannelReleaseIndex.Component sdkComponent,
        ChannelReleaseIndex.Release release,
        SdkDirName sdkDir,
        Logger logger)
    {
        var sdkVersion = sdkComponent.Version;

        var ridString = Utilities.CurrentRID.ToString();
        var sdkInstallPath = UPath.Root / sdkDir.Name;
        var downloadFile = sdkComponent.Files.Single(f => f.Rid == ridString && f.Url.EndsWith(Utilities.ZipSuffix));
        var link = downloadFile.Url;
        logger.Log("Download link: " + link);

        env.Console.WriteLine($"Downloading SDK {sdkVersion} for {ridString}");
        var curMuxerVersion = manifest.MuxerVersion(sdkDir);
        var err = await InstallSdkToDir(
            curMuxerVersion,
            release.Runtime.Version,
            env.HttpClient,
            env.Console,
            link,
            env.DnvmHomeFs,
            sdkInstallPath,
            env.TempFs,
            logger);
        if (err is not null)
        {
            return err;
        }

        SelectCommand.SelectDir(logger, env, manifest.CurrentSdkDir, sdkDir);

        var result = JsonSerializer.Serialize(manifest.ConvertToLatest());
        logger.Log("Existing manifest: " + result);

        if (!manifest.IsSdkInstalled(sdkVersion, sdkDir))
        {
            manifest = manifest with
            {
                InstalledSdks = manifest.InstalledSdks.Add(new InstalledSdk
                {
                    ReleaseVersion = release.ReleaseVersion,
                    RuntimeVersion = release.Runtime.Version,
                    AspNetVersion = release.AspNetCore.Version,
                    SdkVersion = sdkVersion,
                    SdkDirName = sdkDir,
                })
            };

            await @lock.WriteManifest(env, manifest);
        }

        return manifest;
    }

    internal static async Task<InstallError?> InstallSdkToDir(
        SemVersion? curMuxerVersion,
        SemVersion runtimeVersion,
        ScopedHttpClient httpClient,
        IAnsiConsole console,
        string downloadUrl,
        IFileSystem destFs,
        UPath destPath,
        IFileSystem tempFs,
        Logger logger)
    {
        // The Release name does not contain a version
        string archiveName = ConstructArchiveName(versionString: null, Utilities.CurrentRID, Utilities.ZipSuffix);

        // Download and extract into a temp directory
        using var tempDir = new DirectoryResource(Directory.CreateTempSubdirectory().FullName);
        string archivePath = Path.Combine(tempDir.Path, archiveName);
        logger.Log("Archive path: " + archivePath);

        var downloadError = await SpectreUtil.DownloadWithProgress(
            httpClient,
            console,
            logger,
            archivePath,
            downloadUrl,
            "Downloading SDK");

        if (downloadError is not null)
        {
            console.Error(downloadError);
            return InstallError.DownloadFailed;
        }

        console.WriteLine($"Installing to {destPath}");
        string? extractResult = await Utilities.ExtractSdkToDir(
            curMuxerVersion,
            runtimeVersion,
            archivePath,
            tempFs,
            destFs,
            destPath);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            console.Error("Extract failed: " + extractResult);
            return InstallError.ExtractFailed;
        }

        var dotnetExePath = destPath / Utilities.DotnetExeName;
        var dnxScriptPath = destPath / Utilities.DnxScriptName;
        if (!OperatingSystem.IsWindows())
        {
            logger.Log("chmoding downloaded host");
            try
            {
                Utilities.ChmodExec(destFs, dotnetExePath);
                if (destFs.FileExists(dnxScriptPath))
                {
                    Utilities.ChmodExec(destFs, dnxScriptPath);
                }
            }
            catch (Exception e)
            {
                console.Error("chmod failed: " + e.Message);
                return InstallError.ExtractFailed;
            }
        }

        return null;
    }

    internal static string ConstructArchiveName(
        string? versionString,
        RID rid,
        string suffix)
    {
        return versionString is null
            ? $"dotnet-sdk-{rid}{suffix}"
            : $"dotnet-sdk-{versionString}-{rid}{suffix}";
    }
}
