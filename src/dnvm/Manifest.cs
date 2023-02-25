
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using Serde;
using Serde.Json;
using StaticCs;

namespace Dnvm;

[Closed]
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
    public static string GetDesc(this Channel c) => c switch {
        Channel.Latest => "The latest supported version from either the LTS or STS support channels.",
        Channel.Lts => "The latest version in Long-Term support",
        Channel.Sts => "The latest version in Short-Term support",
        Channel.Preview => "The latest preview version",
    };
}

[GenerateSerde]
public sealed partial record Manifest
{
    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 3;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;
    public required ImmutableArray<InstalledSdk> InstalledSdkVersions { get; init; }
    public required ImmutableArray<TrackedChannel> TrackedChannels { get; init; }

    public override string ToString()
    {
        return $"Manifest {{ Version = {Version}, "
            + $"InstalledSdkVersion = [{InstalledSdkVersions.SeqToString()}, "
            + $"TrackedChannels = [{TrackedChannels.SeqToString()}] }}";
    }

    public bool Equals(Manifest? other)
    {
        return other is not null && this.InstalledSdkVersions.SequenceEqual(other.InstalledSdkVersions) &&
            this.TrackedChannels.SequenceEqual(other.TrackedChannels);
    }

    public override int GetHashCode()
    {
        int code = 0;
        foreach (var item in InstalledSdkVersions)
        {
            code = HashCode.Combine(code, item);
        }
        foreach (var item in TrackedChannels)
        {
            code = HashCode.Combine(code, item);
        }
        return code;
    }
}

[GenerateSerde]
public partial record struct TrackedChannel
{
    public required Channel ChannelName { get; init; }
    public required SdkDirName SdkDirName { get; init; }
    public ImmutableArray<string> InstalledSdkVersions { get; init; }

    public bool Equals(TrackedChannel other)
    {
        return this.ChannelName == other.ChannelName &&
            this.SdkDirName == other.SdkDirName &&
            this.InstalledSdkVersions.SequenceEqual(other.InstalledSdkVersions);
    }

    public override int GetHashCode()
    {
        int code = 0;
        code = HashCode.Combine(code, ChannelName);
        code = HashCode.Combine(code, SdkDirName);
        foreach (var item in InstalledSdkVersions)
        {
            code = HashCode.Combine(code, item);
        }
        return code;
    }
}

[GenerateSerde]
public readonly partial record struct InstalledSdk
{
    public string Version { get; init; }
    public required SdkDirName SdkDirName { get; init; }
}

[GenerateSerde]
/// <summary>
/// Holds the simple name of a directory that contains one or more SDKs and lives under DNVM_HOME.
/// This is a wrapper to prevent being used directly as a path.
/// </summary>
public readonly partial record struct SdkDirName(string Name);

public static partial class ManifestUtils
{
    /// <summary>
    /// Either reads a manifest in the current format, or reads a
    /// manifest in the old format and converts it to the new format.
    /// </summary>
    private static Manifest? ReadNewOrOldManifest(string manifestSrc)
    {
        try
        {
            var version = JsonSerializer.Deserialize<ManifestVersionOnly>(manifestSrc).Version;
            return version switch
            {
                // The first version didn't have a version field
                null => JsonSerializer.Deserialize<ManifestV1>(manifestSrc).Convert().Convert(),
                ManifestV2.VersionField => JsonSerializer.Deserialize<ManifestV2>(manifestSrc).Convert(),
                Manifest.VersionField => JsonSerializer.Deserialize<Manifest>(manifestSrc),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to read or create a manifest from the given path. If an IO exception other than <see
    /// cref="DirectoryNotFoundException" /> or <see cref="FileNotFoundException" /> occurs, it will
    /// be rethrown. Throws <see cref="InvalidDataException" />.
    /// </summary>
    public static Manifest ReadOrCreateManifest(string manifestPath)
    {
        string? text = null;
        try
        {
            text = File.ReadAllText(manifestPath);
        }
        // Not found is expected
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException) {}

        if (text is not null)
        {
            var manifestOpt = ManifestUtils.ReadNewOrOldManifest(text);
            if (manifestOpt is null)
            {
                throw new InvalidDataException();
            }
            else
            {
                return manifestOpt;
            }
        }
        else
        {
            return new Manifest() {
                InstalledSdkVersions = ImmutableArray<InstalledSdk>.Empty,
                TrackedChannels = ImmutableArray<TrackedChannel>.Empty
            };
        }
    }

    [GenerateDeserialize]
    private partial struct ManifestVersionOnly
    {
        public int? Version { get; init; }
    }
}
