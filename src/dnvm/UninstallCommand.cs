
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
            Environment.FailFast("Error reading manifest: ", e);
            // unreachable
            return 1;
        }

        var runtimesToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var runtimesToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var sdksToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var aspnetToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var aspnetToRemove = new HashSet<(SemVersion, SdkDirName)>();
        var winToKeep = new HashSet<(SemVersion, SdkDirName)>();
        var winToRemove = new HashSet<(SemVersion, SdkDirName)>();

        foreach (var installed in manifest.InstalledSdkVersions)
        {
            if (installed.SdkVersion == args.SdkVersion)
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

        runtimesToRemove.ExceptWith(runtimesToKeep);
        aspnetToRemove.ExceptWith(aspnetToKeep);
        winToRemove.ExceptWith(winToKeep);

        DeleteSdks(env, sdksToRemove);
        DeleteRuntimes(env, runtimesToRemove);
        DeleteAspnets(env, aspnetToRemove);
        DeleteWins(env, winToRemove);

        manifest = UninstallSdk(manifest, args.SdkVersion);
        env.WriteManifest(manifest);

        return 0;
    }

    private static void DeleteSdks(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> sdks)
    {
        foreach (var (version, dir) in sdks)
        {
            var verString = version.ToString();
            var sdkDir = DnvmEnv.GetSdkPath(dir) / "sdk" / verString;

            env.Vfs.DeleteDirectory(sdkDir, isRecursive: true);
        }
    }

    private static void DeleteRuntimes(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> runtimes)
    {
        foreach (var (version, dir) in runtimes)
        {
            var verString = version.ToString();
            var netcoreappDir = DnvmEnv.GetSdkPath(dir) / "shared" / "Microsoft.NETCore.App" / verString;
            var hostfxrDir = DnvmEnv.GetSdkPath(dir) / "host" / "fxr" / verString;
            var packsHostDir = DnvmEnv.GetSdkPath(dir) / "packs" / $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}" / verString;

            env.Vfs.DeleteDirectory(netcoreappDir, isRecursive: true);
            env.Vfs.DeleteDirectory(hostfxrDir, isRecursive: true);
            env.Vfs.DeleteDirectory(packsHostDir, isRecursive: true);
        }
    }

    private static void DeleteAspnets(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> aspnets)
    {
        foreach (var (version, dir) in aspnets)
        {
            var verString = version.ToString();
            var aspnetDir = DnvmEnv.GetSdkPath(dir) / "shared" / "Microsoft.AspNetCore.App" / verString;
            var templatesDir = DnvmEnv.GetSdkPath(dir) / "templates" / verString;

            env.Vfs.DeleteDirectory(aspnetDir, isRecursive: true);
            env.Vfs.DeleteDirectory(templatesDir, isRecursive: true);
        }
    }

    private static void DeleteWins(DnvmEnv env, IEnumerable<(SemVersion, SdkDirName)> wins)
    {
        foreach (var (version, dir) in wins)
        {
            var verString = version.ToString();
            var winDir = DnvmEnv.GetSdkPath(dir) / "shared" / "Microsoft.WindowsDesktop.App" / verString;

            if (env.Vfs.DirectoryExists(winDir))
            {
                env.Vfs.DeleteDirectory(winDir, isRecursive: true);
            }
        }
    }

    private static Manifest UninstallSdk(Manifest manifest, SemVersion sdkVersion)
    {
        // Delete SDK version from all directories
        var newVersions = manifest.InstalledSdkVersions
            .Where(sdk => sdk.SdkVersion != sdkVersion)
            .ToEq();
        return manifest with {
            InstalledSdkVersions = newVersions,
        };
    }
}