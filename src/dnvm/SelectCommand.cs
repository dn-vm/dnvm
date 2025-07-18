using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Spectre.Console;
using StaticCs;
using Zio;

namespace Dnvm;

public static class SelectCommand
{
    public enum Result
    {
        Success,
        BadDirName,
    }

    public static async Task<Result> Run(DnvmEnv dnvmEnv, Logger logger, SdkDirName sdkDirName)
    {
        using var @lock = await ManifestLock.Acquire(dnvmEnv);
        var manifest = await @lock.ReadManifest(dnvmEnv);
        switch (await RunWithManifest(dnvmEnv, sdkDirName, manifest, logger))
        {
            case Result<Manifest, Result>.Ok(var newManifest):
                await @lock.WriteManifest(dnvmEnv, newManifest);
                return Result.Success;
            case Result<Manifest, Result>.Err(var error):
                return error;
            default:
                throw ExceptionUtilities.Unreachable;
        }
    }

    public static Task<Result<Manifest, Result>> RunWithManifest(DnvmEnv env, SdkDirName newDir, Manifest manifest, Logger logger)
    {
        var console = env.Console;
        var validDirs = manifest.InstalledSdks.Select(s => s.SdkDirName).Distinct().ToList();

        if (!validDirs.Contains(newDir))
        {
            console.Error($"Invalid SDK directory name: {newDir.Name}");
            console.WriteLine("Valid SDK directory names:");
            foreach (var dir in validDirs)
            {
                console.WriteLine($"  {dir.Name}");
            }
            return Task.FromResult<Result<Manifest, Result>>(Result.BadDirName);
        }

        SelectDir(logger, env, manifest.CurrentSdkDir, newDir);
        Result<Manifest, Result> result = manifest with { CurrentSdkDir = newDir };
        return Task.FromResult(result);
    }

    /// <summary>
    /// Change the current SDK directory to the target SDK directory. Doesn't update the manifest.
    ///
    /// This has two implementations - one for Windows and one for Unix. The Unix implementation
    /// uses a symlink to point to the target SDK directory, while the Windows implementation adds
    /// the target SDK directory to the PATH environment variable.
    /// </summary>
    internal static void SelectDir(Logger logger, DnvmEnv dnvmEnv, SdkDirName currentDirName, SdkDirName newDirName)
    {
        if (OperatingSystem.IsWindows())
        {
            RetargetPath(dnvmEnv, currentDirName, newDirName);
        }
        else
        {
            RetargetSymlink(logger, dnvmEnv, currentDirName, newDirName);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RetargetPath(DnvmEnv dnvmEnv, SdkDirName currentDirName, SdkDirName newDirName)
    {
        // First grab the current PATH and look for the existing SDK directory in the PATH. If it
        // exists, remove it.
        var currentPath = dnvmEnv.GetUserEnvVar("PATH");
        List<string> pathDirs = new List<string>();
        if (currentPath != null)
        {
            pathDirs = currentPath.Split(';').ToList();
            var currentDirPath = dnvmEnv.RealPath(DnvmEnv.GetSdkPath(currentDirName));
            pathDirs.Remove(currentDirPath);
        }
        var newDirPath = dnvmEnv.RealPath(DnvmEnv.GetSdkPath(newDirName));
        pathDirs.Insert(0, newDirPath);
        dnvmEnv.SetUserEnvVar("PATH", string.Join(";", pathDirs));
    }

    [UnsupportedOSPlatform("windows")]
    private static void RetargetSymlink(Logger logger, DnvmEnv dnvmEnv, SdkDirName newDirName, SdkDirName sdkDirName)
    {
        var dotnetExePath = DnvmEnv.GetSdkPath(sdkDirName) / Utilities.DotnetExeName;
        var dnxCmdPath = DnvmEnv.GetSdkPath(sdkDirName) / Utilities.DnxScriptName;
        var realDotnetPath = dnvmEnv.RealPath(dotnetExePath);
        logger.Log($"Retargeting symlink in {dnvmEnv.RealPath(UPath.Root)} to {realDotnetPath}");
        if (!dnvmEnv.DnvmHomeFs.FileExists(dotnetExePath))
        {
            logger.Log("SDK install not found, skipping symlink creation.");
            return;
        }

        var homeFs = dnvmEnv.DnvmHomeFs;
        // Delete if it already exists
        try
        {
            homeFs.DeleteFile(DnvmEnv.DotnetSymlinkPath);
            homeFs.DeleteFile(DnvmEnv.DnxSymlinkPath);
        }
        catch { }

        homeFs.CreateSymbolicLink(DnvmEnv.DotnetSymlinkPath, dotnetExePath);
        if (homeFs.FileExists(dnxCmdPath))
        {
            homeFs.CreateSymbolicLink(DnvmEnv.DnxSymlinkPath, dnxCmdPath);
        }
    }
}
