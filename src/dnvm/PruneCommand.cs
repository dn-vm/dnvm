
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Semver;

namespace Dnvm;

public sealed class PruneCommand
{
    public static async Task<int> Run(DnvmEnv env, Logger logger, CommandArguments.PruneArguments args)
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

        var sdksToRemove = GetOutOfDateSdks(manifest);
        foreach (var sdk in sdksToRemove)
        {
            if (args.DryRun)
            {
                Console.WriteLine($"Would remove {sdk}");
            }
            else
            {
                Console.WriteLine($"Removing {sdk}");
                int result = await UninstallCommand.Run(env, logger, new CommandArguments.UninstallArguments
                {
                    SdkVersion = sdk.Version,
                    Dir = sdk.Dir
                });
                if (result != 0)
                {
                    return result;
                }
            }
        }
        return 0;
    }

    public static List<(SemVersion Version, SdkDirName Dir)> GetOutOfDateSdks(Manifest manifest)
    {
        var latestMajorMinorInDirs = new Dictionary<(SdkDirName Dir, string MajorMinor), SemVersion>();
        var sdksToRemove = new List<(SemVersion, SdkDirName)>();
        foreach (var sdk in manifest.InstalledSdkVersions)
        {
            var majorMinor = sdk.SdkVersion.ToMajorMinor();
            var dir = sdk.SdkDirName;
            if (latestMajorMinorInDirs.TryGetValue((sdk.SdkDirName, majorMinor), out var latest))
            {
                int order = sdk.SdkVersion.ComparePrecedenceTo(latest);
                if (order < 0)
                {
                    // This sdk is older than the latest in the same dir
                    sdksToRemove.Add((sdk.SdkVersion, dir));
                }
                else if (order > 0)
                {
                    // This sdk is newer than the latest in the same dir
                    sdksToRemove.Add((latest, dir));
                    latestMajorMinorInDirs[(sdk.SdkDirName, majorMinor)] = sdk.SdkVersion;
                }
                else
                {
                    // same version, do nothing
                }
            }
            else
            {
                latestMajorMinorInDirs[(sdk.SdkDirName, majorMinor)] = sdk.SdkVersion;
            }
        }
        return sdksToRemove;
    }
}