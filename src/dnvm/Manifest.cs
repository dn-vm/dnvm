
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serde;
using Serde.Json;
using StaticCs;
using StaticCs.Collections;

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

[GenerateSerde]
public sealed partial record Manifest
{
    public static readonly Manifest Empty = new();

    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 4;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;

    public SdkDirName CurrentSdkDir { get; init; } = DnvmEnv.DefaultSdkDirName;
    public ImmutableArray<InstalledSdk> InstalledSdkVersions { get; init; } = ImmutableArray<InstalledSdk>.Empty;
    public ImmutableArray<TrackedChannel> TrackedChannels { get; init; } = ImmutableArray<TrackedChannel>.Empty;

    public override string ToString()
    {
        return $"Manifest {{ Version = {Version}, "
            + $"InstalledSdkVersion = [{InstalledSdkVersions.SeqToString()}, "
            + $"TrackedChannels = [{TrackedChannels.SeqToString()}] }}";
    }

    public bool Equals(Manifest? other)
    {
        return other is not null && InstalledSdkVersions.SequenceEqual(other.InstalledSdkVersions) &&
            TrackedChannels.SequenceEqual(other.TrackedChannels);
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

    internal Manifest Untrack(Channel channel)
    {
        return this with
        {
            TrackedChannels = TrackedChannels.Where(c => c.ChannelName != channel).ToImmutableArray()
        };
    }
}

[GenerateSerde]
public readonly partial record struct TrackedChannel()
{
    public required Channel ChannelName { get; init; }
    public required SdkDirName SdkDirName { get; init; }
    public ImmutableArray<string> InstalledSdkVersions { get; init; } = ImmutableArray<string>.Empty;

    public bool Equals(TrackedChannel other)
    {
        return ChannelName == other.ChannelName &&
            SdkDirName == other.SdkDirName &&
            InstalledSdkVersions.SequenceEqual(other.InstalledSdkVersions);
    }

    public override int GetHashCode()
    {
        int code = 0;
        code = HashCode.Combine(code, ChannelName);
        code = HashCode.Combine(code, SdkDirName);
        foreach (string item in InstalledSdkVersions)
        {
            code = HashCode.Combine(code, item);
        }
        return code;
    }
}

[GenerateSerde]
public readonly partial record struct InstalledSdk(string Version)
{
    public SdkDirName SdkDirName { get; init; } = DnvmEnv.DefaultSdkDirName;
}

[GenerateSerde]
/// <summary>
/// Holds the simple name of a directory that contains one or more SDKs and lives under DNVM_HOME.
/// This is a wrapper to prevent being used directly as a path.
/// </summary>
public readonly partial record struct SdkDirName(string Name);

internal static partial class ManifestConvert
{
    internal static Manifest Convert(this ManifestV3 v3) => new Manifest
    {
        InstalledSdkVersions = v3.InstalledSdkVersions.SelectAsArray(v => v.Convert()),
        TrackedChannels = v3.TrackedChannels.SelectAsArray(c => c.Convert()),
    };
}