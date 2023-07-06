
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Serde.Json;
using Spectre.Console;

namespace Dnvm;

public static class SelectCommand
{
    public enum Result
    {
        Success,
        BadDirName,
    }

    public static async Task<Result> Run(GlobalOptions options, Logger logger, CommandArguments.SelectArguments args)
    {
        var newDir = new SdkDirName(args.SdkDirName);
        var manifest = ManifestUtils.ReadOrCreateManifest(options.ManifestPath);
        switch (await RunWithManifest(options.DnvmHome, newDir, manifest, logger))
        {
            case Result<Manifest, Result>.Ok(var newManifest):
                File.WriteAllText(options.ManifestPath, JsonSerializer.Serialize(newManifest));
                return Result.Success;
            case Result<Manifest, Result>.Err(var error):
                return error;
            default:
                throw ExceptionUtilities.Unreachable;
        };
    }

    public static async ValueTask<Result<Manifest, Result>> RunWithManifest(string dnvmHome, SdkDirName newDir, Manifest manifest, Logger logger)
    {
        var validDirs = manifest.TrackedChannels.Select(c => c.SdkDirName).ToList();

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

        return await SelectNewDir(dnvmHome, newDir, manifest);
    }

    /// <summary>
    /// Replaces the dotnet symlink with one pointing to the new SDK and
    /// updates the manifest to reflect the new SDK dir.
    /// </summary>
    private static Task<Manifest> SelectNewDir(string dnvmHome, SdkDirName newDir, Manifest manifest)
    {
        InstallCommand.RetargetSymlink(dnvmHome, newDir);
        return Task.FromResult(manifest with { CurrentSdkDir = newDir });
    }
}