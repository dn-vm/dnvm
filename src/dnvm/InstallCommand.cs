
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using Spectre.Console;
using StaticCs;
using Zio;

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
    public static async Task<Result> Run(DnvmEnv env, Logger logger, CommandArguments.InstallArguments options)
    {
        var sdkDir = options.SdkDir ?? DnvmEnv.DefaultSdkDirName;

        Manifest manifest;
        try
        {
            manifest = await ManifestUtils.ReadOrCreateManifest(env);
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

        var sdkVersion = options.SdkVersion;
        var channel = new Channel.VersionedMajorMinor(sdkVersion.Major, sdkVersion.Minor);

        if (!options.Force && manifest.InstalledSdks.Any(s => s.SdkVersion == sdkVersion && s.SdkDirName == sdkDir))
        {
            logger.Log($"Version {sdkVersion} is already installed in directory '{sdkDir.Name}'." +
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
            logger.Error("Could not fetch the releases index: ");
            logger.Error(e.Message);
            return Result.CouldntFetchReleaseIndex;
        }

        var result = await TryGetReleaseFromIndex(env.HttpClient, versionIndex, channel, sdkVersion)
            ?? await TryGetReleaseFromServer(env, sdkVersion);
        if (result is not ({ } sdkComponent, { } release))
        {
            logger.Error($"SDK version '{sdkVersion}' could not be found in .NET releases index or server.");
            return Result.UnknownChannel;
        }

        var installError = await InstallSdk(env, manifest, sdkComponent, release, sdkDir, logger);
        if (installError is not Result<Manifest, InstallError>.Ok)
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

            return result.FirstOrDefault();
        }

        return null;
    }

    private static async Task<(ChannelReleaseIndex.Component, ChannelReleaseIndex.Release)?> TryGetReleaseFromServer(
        DnvmEnv env,
        SemVersion sdkVersion)
    {
        foreach (var feedUrl in env.DotnetFeedUrls)
        {
            var downloadUrl = $"/Sdk/{sdkVersion}/productCommit-{Utilities.CurrentRID}.json";
            try
            {
                var productCommitData = JsonSerializer.Deserialize<CommitData>(await env.HttpClient.GetStringAsync(feedUrl.TrimEnd('/') + downloadUrl));
                if (productCommitData.Installer.Version != sdkVersion)
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
                continue;
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
        public required Component Installer { get; init; }
        public required Component Sdk { get; init; }
        public required Component Aspnetcore { get; init; }
        public required Component Runtime { get; init; }
        public required Component Windowsdesktop { get; init; }

        [GenerateDeserialize]
        public partial record Component
        {
            [SerdeMemberOptions(DeserializeProxy = typeof(SemVersionProxy))]
            public required SemVersion Version { get; init;  }
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
        logger.Info("Download link: " + link);

        logger.Log($"Downloading SDK {sdkVersion} for {ridString}");
        var error = await InstallSdkToDir(env.HttpClient, link, env.HomeFs, sdkInstallPath, env.TempFs, logger);

        CreateSymlinkIfMissing(env, sdkDir);

        var result = JsonSerializer.Serialize(manifest);
        logger.Info("Existing manifest: " + result);

        if (!manifest.InstalledSdks.Any(s => s.SdkVersion == sdkVersion && s.SdkDirName == sdkDir))
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

            env.WriteManifest(manifest);
        }

        return manifest;
    }

    internal static async Task<InstallError?> InstallSdkToDir(
        ScopedHttpClient httpClient,
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
        logger.Info("Archive path: " + archivePath);

        var downloadError = await logger.DownloadWithProgress(
            httpClient,
            archivePath,
            downloadUrl,
            "Downloading SDK");

        if (downloadError is not null)
        {
            logger.Error(downloadError);
            return InstallError.DownloadFailed;
        }

        logger.Log($"Installing to {destPath}");
        string? extractResult = await Utilities.ExtractArchiveToDir(archivePath, tempFs, destFs, destPath);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            logger.Error("Extract failed: " + extractResult);
            return InstallError.ExtractFailed;
        }

        var dotnetExePath = destPath / Utilities.DotnetExeName;
        if (!OperatingSystem.IsWindows())
        {
            logger.Info("chmoding downloaded host");
            try
            {
                Utilities.ChmodExec(destFs, dotnetExePath);
            }
            catch (Exception e)
            {
                logger.Error("chmod failed: " + e.Message);
                return InstallError.ExtractFailed;
            }
        }

        return null;
    }

    internal static void CreateSymlinkIfMissing(DnvmEnv dnvmFs, SdkDirName sdkDirName)
    {
        var symlinkPath = dnvmFs.HomeFs.ConvertPathToInternal(UPath.Root + Utilities.DotnetSymlinkName);
        if (!File.Exists(symlinkPath))
        {
            RetargetSymlink(dnvmFs, sdkDirName);
        }
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

    /// <summary>
    /// Creates a symlink from the dotnet exe in the dnvm home directory to the dotnet exe in the
    /// sdk install directory.
    /// </summary>
    /// <remarks>
    /// Doesn't use a symlink on Windows because the dotnet muxer doesn't properly resolve through
    /// symlinks.
    /// </remarks>
    internal static void RetargetSymlink(DnvmEnv dnvmFs, SdkDirName sdkDirName)
    {
        var dnvmHome = dnvmFs.HomeFs.ConvertPathToInternal(UPath.Root);
        RetargetSymlink(dnvmHome, sdkDirName);

        static void RetargetSymlink(string dnvmHome, SdkDirName sdkDirName)
        {
            var symlinkPath = Path.Combine(dnvmHome, Utilities.DotnetSymlinkName);
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
    "%~dp0{sdkDirName.Name}\{Utilities.DotnetExeName}" %*
    """);
            }
            else
            {
                // On Unix, we can create a symlink
                File.CreateSymbolicLink(symlinkPath, Path.Combine(sdkInstallDir, Utilities.DotnetExeName));
            }
        }
    }
}
