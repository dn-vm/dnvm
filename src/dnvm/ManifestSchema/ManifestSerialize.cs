
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Serde;
using Serde.Json;

namespace Dnvm;

public static partial class ManifestSerialize
{
    [GenerateDeserialize]
    private sealed partial class ManifestVersionOnly
    {
        public int? Version { get; init; }
    }

    public static string Serialize(Manifest manifest)
    {
        // Convert to the latest version before serializing
        var manifestV9 = manifest.ConvertToLatest();
        return JsonSerializer.Serialize(manifestV9);
    }

    /// <summary>
    /// Either reads a manifest in the current format, or reads a
    /// manifest in the old format and converts it to the new format.
    /// </summary>
    public static async Task<Manifest> DeserializeNewOrOldManifest(
        ScopedHttpClient httpClient,
        string manifestSrc,
        IEnumerable<string> releasesUrls)
    {
        var version = JsonSerializer.Deserialize<ManifestVersionOnly>(manifestSrc).Version;
        // Handle versions that don't need the release index to convert
        Manifest? manifest = version switch
        {
            ManifestV5.VersionField => JsonSerializer.Deserialize<ManifestV5>(manifestSrc).Convert().Convert().Convert().Convert().Convert(),
            ManifestV6.VersionField => JsonSerializer.Deserialize<ManifestV6>(manifestSrc).Convert().Convert().Convert().Convert(),
            ManifestV7.VersionField => JsonSerializer.Deserialize<ManifestV7>(manifestSrc).Convert().Convert().Convert(),
            ManifestV8.VersionField => JsonSerializer.Deserialize<ManifestV8>(manifestSrc).Convert().Convert(),
            ManifestV9.VersionField => JsonSerializer.Deserialize<ManifestV9>(manifestSrc).Convert(),
            _ => null
        };
        if (manifest is not null)
        {
            return manifest;
        }

        // Retrieve release index and convert
        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(httpClient, releasesUrls);
        return version switch
        {
            // The first version didn't have a version field
            null => throw new InvalidDataException("Manifest is invalid: missing version field"),
            ManifestV2.VersionField => (await JsonSerializer.Deserialize<ManifestV2>(manifestSrc)
                .Convert().Convert().Convert(httpClient, releasesIndex)).Convert().Convert().Convert().Convert().Convert(),
            ManifestV3.VersionField => (await JsonSerializer.Deserialize<ManifestV3>(manifestSrc)
                .Convert().Convert(httpClient, releasesIndex)).Convert().Convert().Convert().Convert().Convert(),
            ManifestV4.VersionField => (await JsonSerializer.Deserialize<ManifestV4>(manifestSrc)
                .Convert(httpClient, releasesIndex)).Convert().Convert().Convert().Convert().Convert(),
            _ => throw new InvalidDataException("Unknown manifest version: " + version)
        };
    }
}

public static class ManifestConvert
{
    public static Manifest Convert(this ManifestV9 manifestV9)
    {
        // Migrate PreviewsEnabled from manifest to config file if it's true
        if (manifestV9.PreviewsEnabled)
        {
            try
            {
                var config = DnvmConfigFile.Read();
                if (!config.PreviewsEnabled)
                {
                    // Migrate the setting to the config file
                    DnvmConfigFile.Write(config with { PreviewsEnabled = true });
                }
            }
            catch
            {
                // Best effort migration - ignore errors
            }
        }

        return new Manifest
        {
            CurrentSdkDir = manifestV9.CurrentSdkDir.Convert(),
            InstalledSdks = manifestV9.InstalledSdks.SelectAsArray(sdk => new InstalledSdk
            {
                ReleaseVersion = sdk.ReleaseVersion,
                SdkVersion = sdk.SdkVersion,
                RuntimeVersion = sdk.RuntimeVersion,
                AspNetVersion = sdk.AspNetVersion,
                SdkDirName = sdk.SdkDirName.Convert()
            }),
            RegisteredChannels = manifestV9.RegisteredChannels.SelectAsArray(channel => new RegisteredChannel
            {
                ChannelName = channel.ChannelName,
                SdkDirName = channel.SdkDirName.Convert(),
                InstalledSdkVersions = channel.InstalledSdkVersions.ToEq(),
                Untracked = channel.Untracked
            })
        };
    }

    internal static ManifestV9 ConvertToLatest(this Manifest @this)
    {
        return new ManifestV9
        {
            PreviewsEnabled = false, // Always write false since this is now in config file
            CurrentSdkDir = @this.CurrentSdkDir.ConvertToLatest(),
            InstalledSdks = @this.InstalledSdks.SelectAsArray(sdk => new InstalledSdkV9
            {
                ReleaseVersion = sdk.ReleaseVersion,
                SdkVersion = sdk.SdkVersion,
                RuntimeVersion = sdk.RuntimeVersion,
                AspNetVersion = sdk.AspNetVersion,
                SdkDirName = sdk.SdkDirName.ConvertToLatest()
            }),
            RegisteredChannels = @this.RegisteredChannels.SelectAsArray(channel => new RegisteredChannelV9
            {
                ChannelName = channel.ChannelName,
                SdkDirName = channel.SdkDirName.ConvertToLatest(),
                InstalledSdkVersions = channel.InstalledSdkVersions.ToEq(),
                Untracked = channel.Untracked
            })
        };
    }
}

public static class SdkDirNameConvert
{
    public static SdkDirName Convert(this SdkDirNameV9 sdkDirNameV9)
    {
        return new SdkDirName(sdkDirNameV9.Name);
    }
    public static SdkDirNameV9 ConvertToLatest(this SdkDirName sdkDirName)
    {
        return new SdkDirNameV9(sdkDirName.Name);
    }
}