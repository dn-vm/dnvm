
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Zio;

namespace Dnvm;

public sealed class UninstallCommand
{
    public static async Task<int> Run(DnvmEnv env, Logger logger, CommandArguments.UninstallArguments args)
    {
        Manifest manifest;
        try
        {
            manifest = await env.ReadManifest();
        }
        catch (Exception e)
        {
            logger.Error($"Error reading manifest: {e.Message}");
            throw;
        }

        var runtimesToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var runtimesToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var sdksToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var aspnetToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var aspnetToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var winToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var winToRemove = new HashSet<(SemVersion, SdkDirName)>();

        foreach (var installed in manifest.InstalledSdks)
        {
            if (installed.SdkVersion == args.SdkVersion && (args.Dir is null || installed.SdkDirName == args.Dir))
            {
                sdksToRemove.Add((installed.SdkVersion, installed.SdkDirName));
                runtimesToRemove.Add((installed.RuntimeVersion, installed.SdkDirName));
                aspnetToRemove.Add((installed.AspNetVersion, installed.SdkDirName));
                winToRemove.Add((installed.ReleaseVersion, installed.SdkDirName));
            }
            else
            {
                runtimesToKeep.Add((installed.RuntimeVersion, installed.SdkDirName));
                aspnetToKeep.Add((installed.AspNetVersion, installed.SdkDirName));
                winToKeep.Add((installed.ReleaseVersion, installed.SdkDirName));
            }
        }

        if (sdksToRemove.Count == 0)
        {
            logger.Error($"SDK version {args.SdkVersion} is not installed.");
            return 1;
        }

        runtimesToRemove.ExceptWith(runtimesToKeep);
        aspnetToRemove.ExceptWith(aspnetToKeep);
        winToRemove.ExceptWith(winToKeep);

        DeleteSdks(env, sdksToRemove, logger);
        DeleteRuntimes(env, runtimesToRemove, logger);
        DeleteAspnets(env, aspnetToRemove, logger);
        DeleteWins(env, winToRemove, logger);

        manifest = UninstallSdk(manifest, args.SdkVersion);
        env.WriteManifest(manifest);

        return 0;
    }

    private static void DeleteSdks(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> sdks, Logger logger)
    {
        foreach (var (version, dir) in sdks)
        {
            var verString = version.ToString();
            var sdkDir = DnvmEnv.GetSdkPath(dir) / "sdk" / verString;

            logger.Log($"Deleting SDK {verString} from {dir.Name}");

            env.Vfs.DeleteDirectory(sdkDir, isRecursive: true);
        }
    }

    private static void DeleteRuntimes(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> runtimes, Logger logger)
    {
        foreach (var (version, dir) in runtimes)
        {
            var verString = version.ToString();
            var netcoreappDir = DnvmEnv.GetSdkPath(dir) / "shared" / "Microsoft.NETCore.App" / verString;
            var hostfxrDir = DnvmEnv.GetSdkPath(dir) / "host" / "fxr" / verString;
            var packsHostDir = DnvmEnv.GetSdkPath(dir) / "packs" / $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}" / verString;

            logger.Log($"Deleting Runtime {verString} from {dir.Name}");

            env.Vfs.DeleteDirectory(netcoreappDir, isRecursive: true);
            env.Vfs.DeleteDirectory(hostfxrDir, isRecursive: true);
            env.Vfs.DeleteDirectory(packsHostDir, isRecursive: true);
        }
    }

    private static void DeleteAspnets(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> aspnets, Logger logger)
    {
        foreach (var (version, dir) in aspnets)
        {
            var verString = version.ToString();
            var aspnetDir = DnvmEnv.GetSdkPath(dir) / "shared" / "Microsoft.AspNetCore.App" / verString;
            var templatesDir = DnvmEnv.GetSdkPath(dir) / "templates" / verString;

            logger.Log($"Deleting ASP.NET pack {verString} from {dir.Name}");

            env.Vfs.DeleteDirectory(aspnetDir, isRecursive: true);
            env.Vfs.DeleteDirectory(templatesDir, isRecursive: true);
        }
    }

    private static void DeleteWins(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> wins, Logger logger)
    {
        foreach (var (version, dir) in wins)
        {
            var verString = version.ToString();
            var winDir = DnvmEnv.GetSdkPath(dir) / "shared" / "Microsoft.WindowsDesktop.App" / verString;

            if (env.Vfs.DirectoryExists(winDir))
            {
                logger.Log($"Deleting Windows Desktop pack {verString} from {dir.Name}");

                env.Vfs.DeleteDirectory(winDir, isRecursive: true);
            }
        }
    }

    private static Manifest UninstallSdk(Manifest manifest, SemVersion sdkVersion)
    {
        // Delete SDK version from all directories
        var newVersions = manifest.InstalledSdks
            .Where(sdk => sdk.SdkVersion != sdkVersion)
            .ToEq();
        return manifest with {
            InstalledSdks = newVersions,
        };
    }
}