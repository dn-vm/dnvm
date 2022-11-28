
using System;
using System.Collections.Immutable;
using System.IO;
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
    private static Manifest? ReadNewOrOldManifest(string manifestSrc)
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
                InstalledVersions = ImmutableArray<string>.Empty,
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