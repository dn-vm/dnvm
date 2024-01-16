
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serde.Json;
using Spectre.Console;
using StaticCs;
using Zio;

namespace Dnvm;

public static class InstallCommand
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
        var channel = new Channel.Versioned(sdkVersion.Major, sdkVersion.Minor);

        if (!options.Force && manifest.InstalledSdks.Any(s => s.SdkVersion == sdkVersion && s.SdkDirName == sdkDir))
        {
            logger.Log($"Version {sdkVersion} is already installed in directory '{sdkDir.Name}'." +
                " Skipping installation. To install anyway, pass --force.");
        }

        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await DotnetReleasesIndex.FetchLatestIndex(env.DotnetFeedUrl);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.Error("Could not fetch the releases index: ");
            logger.Error(e.Message);
            return Result.CouldntFetchReleaseIndex;
        }

        var channelIndex = versionIndex.GetChannelIndex(channel);
        if (channelIndex is null)
        {
            logger.Error($"Channel '{channel}' not found in .NET releases index");
            return Result.UnknownChannel;
        }

        var release = JsonSerializer.Deserialize<ChannelReleaseIndex>(
            await Program.HttpClient.GetStringAsync(channelIndex.ChannelReleaseIndexUrl))
            .Releases.Single(r => r.Sdk.Version == sdkVersion);

        var installError = await InstallSdk(env, manifest, release, sdkDir, logger);
        if (installError is not null)
        {
            return Result.InstallError;
        }

        return Result.Success;
    }

    [Closed]
    private enum InstallError
    {
        DownloadFailed,
        ExtractFailed
    }

    /// <summary>
    /// Install the given SDK inside the given directory, and update the manifest. Does not update the channel manifest.
    /// </summary>
    /// <exception cref="InvalidOperationException">Throws when manifest already contains the given SDK.</exception>
    private static async Task<InstallError?> InstallSdk(
        DnvmEnv env,
        Manifest manifest,
        ChannelReleaseIndex.Release release,
        SdkDirName sdkDir,
        Logger logger)
    {
        var sdkVersion = release.Sdk.Version;
        if (manifest.InstalledSdks.Any(s => s.SdkVersion == sdkVersion && s.SdkDirName == sdkDir))
        {
            throw new InvalidOperationException($"SDK version {sdkVersion} is already installed in directory '{sdkDir.Name}'");
        }

        var ridString = Utilities.CurrentRID.ToString();
        var sdkInstallPath = UPath.Root / sdkDir.Name;

        // The Release name does not contain a version
        string archiveName = ConstructArchiveName(versionString: null, Utilities.CurrentRID, Utilities.ZipSuffix);

        // Download and extract into a temp directory
        using var tempDir = new DirectoryResource(Directory.CreateTempSubdirectory().FullName);
        string archivePath = Path.Combine(tempDir.Path, archiveName);
        logger.Info("Archive path: " + archivePath);

        var downloadFile = release.Sdk.Files.Single(f => f.Name == archiveName);
        var link = downloadFile.Url;
        logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        logger.Info("Existing manifest: " + result);

        logger.Log($"Downloading dotnet SDK {release.Sdk.Version}");

        var downloadError = await logger.Console.DownloadWithProgress(Program.HttpClient, archivePath, link);
        if (downloadError is not null)
        {
            logger.Error(downloadError);
            return InstallError.DownloadFailed;
        }

        logger.Log($"Installing to {sdkInstallPath}");
        string? extractResult = await Utilities.ExtractArchiveToDir(archivePath, env, sdkInstallPath);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            logger.Error("Extract failed: " + extractResult);
            return InstallError.ExtractFailed;
        }

        var dotnetExePath = sdkInstallPath / Utilities.DotnetExeName;
        if (!OperatingSystem.IsWindows())
        {
            logger.Info("chmoding downloaded host");
            try
            {
                Utilities.ChmodExec(env.Vfs, dotnetExePath);
            }
            catch (Exception e)
            {
                logger.Error("chmod failed: " + e.Message);
                return InstallError.ExtractFailed;
            }
        }
        CreateSymlinkIfMissing(env, sdkDir);

        manifest = manifest with { InstalledSdks = manifest.InstalledSdks.Add(new InstalledSdk {
            ReleaseVersion = release.ReleaseVersion,
            RuntimeVersion = release.Runtime.Version,
            AspNetVersion = release.AspNetCore.Version,
            SdkVersion = sdkVersion,
            SdkDirName = sdkDir,
            })
        };

        env.WriteManifest(manifest);

        return null;
    }


    internal static void CreateSymlinkIfMissing(DnvmEnv dnvmFs, SdkDirName sdkDirName)
    {
        var symlinkPath = dnvmFs.Vfs.ConvertPathToInternal(UPath.Root + Utilities.DotnetSymlinkName);
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
        var dnvmHome = dnvmFs.Vfs.ConvertPathToInternal(UPath.Root);
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