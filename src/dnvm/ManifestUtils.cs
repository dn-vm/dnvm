
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Channels;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using StaticCs;
using StaticCs.Collections;

namespace Dnvm;

[GenerateSerde]
/// <summary>
/// Holds the simple name of a directory that contains one or more SDKs and lives under DNVM_HOME.
/// This is a wrapper to prevent being used directly as a path.
/// </summary>
public readonly partial record struct SdkDirName(string Name)
{
    public string Name { get; init; } = Name.ToLowerInvariant();
}


public static partial class ManifestUtils
{
    public static async Task<Manifest> ReadOrCreateManifest(DnvmEnv fs)
    {
        try
        {
            return await fs.ReadManifest();
        }
        // Not found is expected
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException) { }

        return Manifest.Empty;
    }

    public static EqArray<RegisteredChannel> TrackedChannels(this Manifest manifest)
    {
        return manifest.RegisteredChannels.Where(x => !x.Untracked).ToEq();
    }

    public static Manifest AddSdk(this Manifest manifest, SemVersion semVersion, Channel c)
        => AddSdk(manifest, semVersion, c, DnvmEnv.DefaultSdkDirName);

    public static Manifest AddSdk(this Manifest manifest, SemVersion semVersion, Channel c, SdkDirName sdkDirName)
    {
        var installedSdk = new InstalledSdk() {
            SdkDirName = sdkDirName,
            SdkVersion = semVersion,
            RuntimeVersion = semVersion,
            AspNetVersion = semVersion,
            ReleaseVersion = semVersion,
        };
        return manifest.AddSdk(installedSdk, c);
    }

    public static Manifest AddSdk(this Manifest manifest, InstalledSdk sdk, Channel c)
    {
        var installedSdks = manifest.InstalledSdks;
        if (!installedSdks.Contains(sdk))
        {
            installedSdks = installedSdks.Add(sdk);
        }
        EqArray<RegisteredChannel> trackedChannels = manifest.RegisteredChannels;
        if (trackedChannels.FirstOrNull(x => x.ChannelName == c && x.SdkDirName == sdk.SdkDirName) is { } oldTracked)
        {
            trackedChannels = manifest.RegisteredChannels;
            var installedSdkVersions = oldTracked.InstalledSdkVersions;
            var newTracked = installedSdkVersions.Contains(sdk.SdkVersion)
                ? oldTracked
                : oldTracked with {
                    InstalledSdkVersions = installedSdkVersions.Add(sdk.SdkVersion)
                };
            trackedChannels = trackedChannels.Replace(oldTracked, newTracked);
        }
        else
        {
            trackedChannels = trackedChannels.Add(new RegisteredChannel {
                ChannelName = c,
                SdkDirName = sdk.SdkDirName,
                InstalledSdkVersions = [ sdk.SdkVersion ]
            });
        }
        return manifest with {
            InstalledSdks = installedSdks,
            RegisteredChannels = trackedChannels,
        };
    }

    /// <summary>
    /// Either reads a manifest in the current format, or reads a
    /// manifest in the old format and converts it to the new format.
    /// </summary>
    public static async Task<Manifest> DeserializeNewOrOldManifest(string manifestSrc, string releasesUrl)
    {
        var version = JsonSerializer.Deserialize<ManifestVersionOnly>(manifestSrc).Version;
        // Handle versions that don't need the release index to convert
        Manifest? manifest = version switch {
            ManifestV5.VersionField => JsonSerializer.Deserialize<ManifestV5>(manifestSrc).Convert().Convert(),
            ManifestV6.VersionField => JsonSerializer.Deserialize<ManifestV6>(manifestSrc).Convert(),
            Manifest.VersionField => JsonSerializer.Deserialize<Manifest>(manifestSrc),
            _ => null
        };
        if (manifest is not null)
        {
            return manifest;
        }
        // Retrieve release index and convert
        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(releasesUrl);
        return version switch
        {
            // The first version didn't have a version field
            null => (await JsonSerializer.Deserialize<ManifestV1>(manifestSrc).Convert().Convert().Convert().Convert(releasesIndex)).Convert().Convert(),
            ManifestV2.VersionField => (await JsonSerializer.Deserialize<ManifestV2>(manifestSrc).Convert().Convert().Convert(releasesIndex)).Convert().Convert(),
            ManifestV3.VersionField => (await JsonSerializer.Deserialize<ManifestV3>(manifestSrc).Convert().Convert(releasesIndex)).Convert().Convert(),
            ManifestV4.VersionField => (await JsonSerializer.Deserialize<ManifestV4>(manifestSrc).Convert(releasesIndex)).Convert().Convert(),
            _ => throw new InvalidDataException("Unknown manifest version: " + version)
        };
    }

    [GenerateDeserialize]
    private readonly partial struct ManifestVersionOnly
    {
        public int? Version { get; init; }
    }
}