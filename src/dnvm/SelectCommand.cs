
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Serde.Json;
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
        var manifest = await dnvmEnv.ReadManifest();
        switch (await RunWithManifest(dnvmEnv, sdkDirName, manifest, logger))
        {
            case Result<Manifest, Result>.Ok(var newManifest):
                dnvmEnv.WriteManifest(newManifest);
                return Result.Success;
            case Result<Manifest, Result>.Err(var error):
                return error;
            default:
                throw ExceptionUtilities.Unreachable;
        };
    }

    public static async ValueTask<Result<Manifest, Result>> RunWithManifest(DnvmEnv env, SdkDirName newDir, Manifest manifest, Logger logger)
    {
        var validDirs = manifest.RegisteredChannels.Select(c => c.SdkDirName).ToList();

        if (!validDirs.Contains(newDir))
        {
            logger.Log($"Invalid SDK directory name: {newDir.Name}");
            logger.Log("Valid SDK directory names:");
            foreach (var dir in validDirs)
            {
                logger.Log($"  {dir.Name}");
            }
            return Result.BadDirName;
        }

        return await SelectNewDir(logger, env, newDir, manifest);
    }

    /// <summary>
    /// Replaces the dotnet symlink with one pointing to the new SDK and
    /// updates the manifest to reflect the new SDK dir.
    /// </summary>
    private static Task<Manifest> SelectNewDir(Logger logger, DnvmEnv env, SdkDirName newDir, Manifest manifest)
    {
        InstallCommand.RetargetSymlink(logger, env, newDir);
        return Task.FromResult(manifest with { CurrentSdkDir = newDir });
    }
}