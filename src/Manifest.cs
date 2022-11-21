
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.PortableExecutable;
using Serde;
using Serde.Json;

namespace Dnvm;

public enum Channel
{
    /// <summary>
    /// Latest supported version from either the LTS or STS support channels.
    /// </summary>
    Latest,
    /// <summary>
    /// Newest LTS-supported release.
    /// </summary>
    Lts,
    /// <summary>
    /// Newest "preview" release, not including nightly builds.
    /// </summary>
    Preview,
}

public static partial class ManifestUtils
{
    public const string FileName = "dnvmManifest.json";
    /// <summary>
    /// Either reads a manifest in the current format, or reads a
    /// manifest in the old format and converts it to the new format.
    /// </summary>
    public static Manifest? ReadNewOrOldManifest(string manifestSrc)
    {
        try
        {
            var version = JsonSerializer.Deserialize<ManifestVersionOnly>(manifestSrc).Version;
            return version switch
            {
                null => JsonSerializer.Deserialize<ManifestV1>(manifestSrc).Convert(),// The first version didn't have a version field
                2 => JsonSerializer.Deserialize<Manifest>(manifestSrc),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    [GenerateDeserialize]
    private partial struct ManifestVersionOnly
    {
        public int? Version { get; init; }
    }
}


[GenerateSerde]
public sealed partial record Manifest
{
    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => 2;
    public required ImmutableArray<string> InstalledVersions { get; init; }
    public required ImmutableArray<TrackedChannel> TrackedChannels { get; init; }
}

[GenerateSerde]
public partial record struct TrackedChannel
{
    public Channel ChannelName { get; init; }
    public ImmutableArray<string> InstalledVersions { get; init; }
}