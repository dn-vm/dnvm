
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Spectre.Console;
using Zio;

namespace Dnvm;

public sealed class UninstallCommand
{
    public static async Task<int> Run(DnvmEnv env, Logger logger, SemVersion sdkVersion, SdkDirName? dir = null)
    {
        using var @lock = await ManifestLock.Acquire(env);
        return await Run(@lock, env, logger, sdkVersion, dir);
    }

    public static async Task<int> Run(ManifestLock @lock, DnvmEnv env, Logger logger, SemVersion sdkVersion, SdkDirName? dir = null)
    {
        Manifest manifest;
        try
        {
            manifest = await @lock.ReadManifest(env);
        }
        catch (Exception e)
        {
            env.Console.Error($"Error reading manifest: {e.Message}");
            throw;
        }

        var runtimesToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var runtimesToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var sdksToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var aspnetToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var aspnetToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var winToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var winToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var winDirsWithUnknownVersion = new HashSet<SdkDirName>();
        var bandsToKeep = new HashSet<(string, SdkDirName)>();
        var bandsToRemove = new HashSet<(string, SdkDirName)>();
        var bandDirsWithUnknownManifests = new HashSet<SdkDirName>();

        foreach (var installed in manifest.InstalledSdks)
        {
            if (installed.SdkVersion == sdkVersion && (dir is null || installed.SdkDirName == dir))
            {
                sdksToRemove.Add((installed.SdkVersion, installed.SdkDirName));
                runtimesToRemove.Add((installed.RuntimeVersion, installed.SdkDirName));
                aspnetToRemove.Add((installed.AspNetVersion, installed.SdkDirName));
                if (installed.WindowsDesktopVersion is { } windowsDesktopVersion)
                {
                    winToRemove.Add((windowsDesktopVersion, installed.SdkDirName));
                }
                bandsToRemove.UnionWith(installed.SdkManifestBands.Select(b => (b, installed.SdkDirName)));
            }
            else
            {
                runtimesToKeep.Add((installed.RuntimeVersion, installed.SdkDirName));
                aspnetToKeep.Add((installed.AspNetVersion, installed.SdkDirName));
                if (installed.WindowsDesktopVersion is { } windowsDesktopVersion)
                {
                    winToKeep.Add((windowsDesktopVersion, installed.SdkDirName));
                }
                else
                {
                    winDirsWithUnknownVersion.Add(installed.SdkDirName);
                }
                bandsToKeep.UnionWith(installed.SdkManifestBands.Select(b => (b, installed.SdkDirName)));
                if (!installed.SdkManifestBandsKnown)
                {
                    bandDirsWithUnknownManifests.Add(installed.SdkDirName);
                }
            }
        }

        if (sdksToRemove.Count == 0)
        {
            env.Console.Error($"SDK version {sdkVersion} is not installed.");
            return 1;
        }

        runtimesToRemove.ExceptWith(runtimesToKeep);
        aspnetToRemove.ExceptWith(aspnetToKeep);
        winToRemove.ExceptWith(winToKeep);
        winToRemove.RemoveWhere(win => winDirsWithUnknownVersion.Contains(win.Item2));
        bandsToRemove.ExceptWith(bandsToKeep);
        bandsToRemove.RemoveWhere(band => bandDirsWithUnknownManifests.Contains(band.Item2));

        DeleteSdks(env, sdksToRemove, logger);
        DeleteRuntimes(env, runtimesToRemove, logger);
        DeleteAspnets(env, aspnetToRemove, logger);
        DeleteWins(env, winToRemove, logger);
        DeleteSdkManifests(env, bandsToRemove, logger);

        manifest = UninstallSdks(manifest, sdksToRemove);
        await @lock.WriteManifest(env, manifest);

        return 0;
    }

    private static void DeleteSdks(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> sdks, Logger logger)
    {
        foreach (var (version, dir) in sdks)
        {
            var verString = version.ToString();
            var sdkDir = DnvmEnv.GetSdkPath(dir) / "sdk" / verString;

            env.Console.WriteLine($"Deleting SDK {verString} from {dir.Name}");

            TryDeleteDirectory(env, sdkDir);
        }
    }

    private static void DeleteRuntimes(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> runtimes, Logger logger)
    {
        foreach (var (version, dir) in runtimes)
        {
            var verString = version.ToString();
            var sdkPath = DnvmEnv.GetSdkPath(dir);
            var hostPackId = $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}";
            const string refPackId = "Microsoft.NETCore.App.Ref";
            var netcoreappDir = sdkPath / "shared" / "Microsoft.NETCore.App" / verString;
            var hostfxrDir = sdkPath / "host" / "fxr" / verString;
            var packsHostDir = sdkPath / "packs" / hostPackId / verString;
            var packsRefDir = sdkPath / "packs" / refPackId / verString;

            env.Console.WriteLine($"Deleting Runtime {verString} from {dir.Name}");

            TryDeleteDirectory(env, netcoreappDir);
            TryDeleteDirectory(env, hostfxrDir);
            if (!IsPackReferencedByWorkloads(env, sdkPath, hostPackId, version))
            {
                TryDeleteDirectory(env, packsHostDir);
            }
            if (!IsPackReferencedByWorkloads(env, sdkPath, refPackId, version))
            {
                TryDeleteDirectory(env, packsRefDir);
            }
        }
    }

    private static void DeleteAspnets(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> aspnets, Logger logger)
    {
        foreach (var (version, dir) in aspnets)
        {
            var verString = version.ToString();
            var sdkPath = DnvmEnv.GetSdkPath(dir);
            const string refPackId = "Microsoft.AspNetCore.App.Ref";
            var aspnetDir = sdkPath / "shared" / "Microsoft.AspNetCore.App" / verString;
            var templatesDir = sdkPath / "templates" / verString;
            var packsRefDir = sdkPath / "packs" / refPackId / verString;

            env.Console.WriteLine($"Deleting ASP.NET pack {verString} from {dir.Name}");

            TryDeleteDirectory(env, aspnetDir);
            TryDeleteDirectory(env, templatesDir);
            if (!IsPackReferencedByWorkloads(env, sdkPath, refPackId, version))
            {
                TryDeleteDirectory(env, packsRefDir);
            }
        }
    }

    private static void DeleteWins(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> wins, Logger logger)
    {
        foreach (var (version, dir) in wins)
        {
            var verString = version.ToString();
            var sdkPath = DnvmEnv.GetSdkPath(dir);
            const string refPackId = "Microsoft.WindowsDesktop.App.Ref";
            var winDir = sdkPath / "shared" / "Microsoft.WindowsDesktop.App" / verString;
            var packsRefDir = sdkPath / "packs" / refPackId / verString;

            if (env.DnvmHomeFs.DirectoryExists(winDir))
            {
                env.Console.WriteLine($"Deleting Windows Desktop pack {verString} from {dir.Name}");
                TryDeleteDirectory(env, winDir);
            }

            if (env.DnvmHomeFs.DirectoryExists(packsRefDir)
                && !IsPackReferencedByWorkloads(env, sdkPath, refPackId, version))
            {
                TryDeleteDirectory(env, packsRefDir);
            }
        }
    }

    private static void DeleteSdkManifests(DnvmEnv env, IEnumerable<(string, SdkDirName)> bands, Logger logger)
    {
        foreach (var (band, dir) in bands)
        {
            var sdkPath = DnvmEnv.GetSdkPath(dir);
            var manifestsDir = sdkPath / "sdk-manifests" / band;

            if (env.DnvmHomeFs.DirectoryExists(manifestsDir)
                && !IsFeatureBandReferencedByWorkloads(env, sdkPath, band))
            {
                env.Console.WriteLine($"Deleting workload manifests for feature band {band} from {dir.Name}");
                TryDeleteDirectory(env, manifestsDir);
            }
        }
    }

    private static bool IsFeatureBandReferencedByWorkloads(DnvmEnv env, UPath sdkPath, string band)
    {
        var workloadsMetadataDir = sdkPath / "metadata" / "workloads";
        if (!env.DnvmHomeFs.DirectoryExists(workloadsMetadataDir))
        {
            return false;
        }

        return env.DnvmHomeFs
            .EnumerateFiles(workloadsMetadataDir, "*", SearchOption.AllDirectories)
            .Any(file => file.FullName.Split('/').Contains(band, StringComparer.Ordinal));
    }

    private static bool IsPackReferencedByWorkloads(
        DnvmEnv env,
        UPath sdkPath,
        string packId,
        SemVersion version)
    {
        var installedPackVersionDir = sdkPath / "metadata" / "workloads" / "InstalledPacks"
            / "v1" / packId / version.ToString();
        return env.DnvmHomeFs.DirectoryExists(installedPackVersionDir);
    }

    private static Manifest UninstallSdks(
        Manifest manifest,
        HashSet<(SemVersion Version, SdkDirName Dir)> sdksToRemove)
    {
        var newVersions = manifest.InstalledSdks
            .Where(sdk => !sdksToRemove.Contains((sdk.SdkVersion, sdk.SdkDirName)))
            .ToEq();

        var updatedChannels = manifest.RegisteredChannels.Select(channel =>
        {
            var updatedInstalledVersions = channel.InstalledSdkVersions
                .Where(version => !sdksToRemove.Contains((version, channel.SdkDirName)))
                .ToEq();
            return channel with { InstalledSdkVersions = updatedInstalledVersions };
        }).ToEq();

        return manifest with {
            InstalledSdks = newVersions,
            RegisteredChannels = updatedChannels,
        };
    }

    private static void TryDeleteDirectory(DnvmEnv env, UPath directory)
    {
        try
        {
            env.DnvmHomeFs.DeleteDirectory(directory, isRecursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            env.Console.Warn($"Directory {directory} not found, skipping");
        }
    }
}