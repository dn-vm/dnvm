
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
public readonly partial record struct SdkDirName(string Name);

[Closed]
[GenerateSerde]
public enum Channel
{
    /// <summary>
    /// Latest supported version from either the LTS or STS support channels.
    /// </summary>
    Latest,
    /// <summary>
    /// Newest Long Term Support release.
    /// </summary>
    Lts,
    /// <summary>
    /// Newest Short Term Support release.
    /// </summary>
    Sts,

    /// </summary>
    /// <summary>
    /// Newest "preview" release, not including nightly builds.
    /// </summary>
    Preview,
}

public static class Channels
{
    public static string GetDesc(this Channel c) => c switch
    {
        Channel.Latest => "The latest supported version from either the LTS or STS support channels.",
        Channel.Lts => "The latest version in Long-Term support",
        Channel.Sts => "The latest version in Short-Term support",
        Channel.Preview => "The latest preview version",
        _ => throw new NotImplementedException(),
    };

    public static string GetLowerName(this Channel c) => c.ToString().ToLowerInvariant();
}


public static partial class ManifestUtils
{
    /// <summary>
    /// Reads a manifest (any version) from the given path and returns
    /// an up-to-date <see cref="Manifest" /> (latest version).
    /// Throws <see cref="InvalidDataException" /> if the manifest is invalid.
    /// </summary>
    public static Manifest ReadManifest(string manifestPath)
    {
        string text = File.ReadAllText(manifestPath);
        return DeserializeNewOrOldManifest(text) ?? throw new InvalidDataException();
    }

    public static Manifest ReadOrCreateManifest(DnvmEnv fs)
    {
        try
        {
            return fs.ReadManifest();
        }
        // Not found is expected
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException) { }

        return Manifest.Empty;
    }

    /// <summary>
    /// Tries to read or create a manifest from the given path. If an IO exception other than <see
    /// cref="DirectoryNotFoundException" /> or <see cref="FileNotFoundException" /> occurs, it will
    /// be rethrown. Throws <see cref="InvalidDataException" />.
    /// </summary>
    public static Manifest ReadOrCreateManifest(string manifestPath)
    {
        try
        {
            return ReadManifest(manifestPath);
        }
        // Not found is expected
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException) { }

        return new Manifest()
        {
            InstalledSdkVersions = [],
            TrackedChannels = []
        };
    }

    public static Manifest AddSdk(this Manifest manifest, InstalledSdk sdk, Channel c)
    {
        Manifest newManifest;
        if (manifest.TrackedChannels.FirstOrNull(x => x.ChannelName == c) is { } trackedChannel)
        {
            if (trackedChannel.InstalledSdkVersions.Contains(sdk.Version))
            {
                return manifest;
            }
            newManifest = manifest with
            {
                TrackedChannels = manifest.TrackedChannels.Select(x => x.ChannelName == c
                    ? x with { InstalledSdkVersions = x.InstalledSdkVersions.Add(sdk.Version) }
                    : x).ToEq(),
                InstalledSdkVersions = manifest.InstalledSdkVersions.Add(sdk)
            };
        }
        else
        {
            newManifest = manifest with
            {
                TrackedChannels = manifest.TrackedChannels.Add(new TrackedChannel()
                {
                    ChannelName = c,
                    SdkDirName = sdk.SdkDirName,
                    InstalledSdkVersions = ImmutableArray.Create(sdk.Version).ToEq()
                }),
                InstalledSdkVersions = manifest.InstalledSdkVersions.Add(sdk)
            };
        }
        return newManifest;
    }

    /// <summary>
    /// Either reads a manifest in the current format, or reads a
    /// manifest in the old format and converts it to the new format.
    /// </summary>
    public static Manifest? DeserializeNewOrOldManifest(string manifestSrc)
    {
        try
        {
            var version = JsonSerializer.Deserialize<ManifestVersionOnly>(manifestSrc).Version;
            return version switch
            {
                // The first version didn't have a version field
                null => JsonSerializer.Deserialize<ManifestV1>(manifestSrc).Convert().Convert().Convert(),
                ManifestV2.VersionField => JsonSerializer.Deserialize<ManifestV2>(manifestSrc).Convert().Convert(),
                ManifestV3.VersionField => JsonSerializer.Deserialize<ManifestV3>(manifestSrc).Convert(),
                Manifest.VersionField => JsonSerializer.Deserialize<Manifest>(manifestSrc),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    [GenerateDeserialize]
    private readonly partial struct ManifestVersionOnly
    {
        public int? Version { get; init; }
    }
}