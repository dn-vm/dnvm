
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Semver;

namespace Dnvm;

public sealed class PruneCommand
{
    public sealed record Options
    {
        public bool Verbose { get; init; } = false;
        public bool DryRun { get; init; } = false;
    }

    public static Task<int> Run(DnvmEnv env, Logger logger, DnvmSubCommand.PruneArgs args)
    {
        return Run(env, logger, new Options
        {
            Verbose = args.Verbose ?? false,
            DryRun = args.DryRun ?? false
        });
    }

    public static async Task<int> Run(DnvmEnv env, Logger logger, Options options)
    {
        using var @lock = await ManifestLock.Acquire(env);
        Manifest manifest;
        try
        {
            manifest = await @lock.ReadManifest(env);
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
            if (options.DryRun)
            {
                Console.WriteLine($"Would remove {sdk}");
            }
            else
            {
                // Check if the SDK is actually in InstalledSdks. It may not be if the manifest
                // was corrupted by a bug fixed in https://github.com/dn-vm/dnvm/pull/274.
                // In that case, we just need to clean up the stale entry from RegisteredChannels.
                if (!manifest.IsSdkInstalled(sdk.Version, sdk.Dir))
                {
                    env.Console.Warn($"SDK {sdk.Version} was not found in installed SDKs, cleaning up stale manifest entry.");
                    manifest = RemoveSdkFromChannels(manifest, sdk.Version);
                    await @lock.WriteManifest(env, manifest);
                    continue;
                }

                Console.WriteLine($"Removing {sdk}");
                int result = await UninstallCommand.Run(@lock, env, logger, sdk.Version, sdk.Dir);
                if (result != 0)
                {
                    return result;
                }
                // Re-read manifest after uninstall since it was modified
                manifest = await @lock.ReadManifest(env);
            }
        }
        return 0;
    }

    public static List<(SemVersion Version, SdkDirName Dir)> GetOutOfDateSdks(Manifest manifest)
    {
        var sdksToRemove = new List<(SemVersion, SdkDirName)>();

        // Get all tracked channels (exclude untracked ones)
        var trackedChannels = manifest.TrackedChannels();

        // For each tracked channel, find versions to prune within that channel
        foreach (var channel in trackedChannels)
        {
            // Group SDKs installed through this channel by major.minor version
            var channelSdksByMajorMinor = new Dictionary<string, List<SemVersion>>();

            foreach (var sdkVersion in channel.InstalledSdkVersions)
            {
                var majorMinor = sdkVersion.ToMajorMinor();
                if (!channelSdksByMajorMinor.ContainsKey(majorMinor))
                {
                    channelSdksByMajorMinor[majorMinor] = new List<SemVersion>();
                }
                channelSdksByMajorMinor[majorMinor].Add(sdkVersion);
            }

            // For each major.minor group, keep only the latest version
            foreach (var (majorMinor, versions) in channelSdksByMajorMinor)
            {
                if (versions.Count > 1)
                {
                    // Sort versions and mark all but the latest for removal
                    var sortedVersions = versions.OrderBy(v => v, SemVersion.SortOrderComparer).ToList();
                    for (int i = 0; i < sortedVersions.Count - 1; i++)
                    {
                        sdksToRemove.Add((sortedVersions[i], channel.SdkDirName));
                    }
                }
            }
        }

        return sdksToRemove;
    }

    /// <summary>
    /// Removes an SDK version from all RegisteredChannels.InstalledSdkVersions.
    /// This is used to clean up stale entries left by a bug fixed in
    /// https://github.com/dn-vm/dnvm/pull/274.
    /// </summary>
    private static Manifest RemoveSdkFromChannels(Manifest manifest, SemVersion sdkVersion)
    {
        var updatedChannels = manifest.RegisteredChannels.Select(channel =>
        {
            var updatedInstalledVersions = channel.InstalledSdkVersions
                .Where(version => version != sdkVersion)
                .ToEq();
            return channel with { InstalledSdkVersions = updatedInstalledVersions };
        }).ToEq();

        return manifest with { RegisteredChannels = updatedChannels };
    }
}