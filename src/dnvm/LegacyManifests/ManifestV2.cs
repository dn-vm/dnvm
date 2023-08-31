
using System;
using System.Collections.Immutable;
using System.Linq;
using Serde;

namespace Dnvm;

[GenerateSerde]
internal sealed partial record ManifestV2
{
    public Manifest Convert()
    {
        return new Manifest {
            InstalledSdkVersions = InstalledSdkVersions.Select(v => new InstalledSdk() {
                Version = v,
                // Before V3, all SDKs were installed to the default dir
                SdkDirName = DnvmEnv.DefaultSdkDirName
            }).ToImmutableArray(),
            TrackedChannels = TrackedChannels.Select(c => new TrackedChannel {
                ChannelName = c.ChannelName,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = c.InstalledSdkVersions
            }).ToImmutableArray(),
        };
    }

    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 2;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;
    public required ImmutableArray<string> InstalledSdkVersions { get; init; }
    public required ImmutableArray<TrackedChannelV2> TrackedChannels { get; init; }

    public override string ToString()
    {
        return $"Manifest {{ Version = {Version}, "
            + $"InstalledSdkVersion = [{InstalledSdkVersions.SeqToString()}, "
            + $"TrackedChannels = [{TrackedChannels.SeqToString()}] }}";
    }

    public bool Equals(ManifestV2? other)
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
internal partial record struct TrackedChannelV2
{
    public Channel ChannelName { get; init; }
    public ImmutableArray<string> InstalledSdkVersions { get; init; }

    public bool Equals(TrackedChannelV2 other)
    {
        return this.ChannelName == other.ChannelName &&
            this.InstalledSdkVersions.SequenceEqual(other.InstalledSdkVersions);
    }

    public override int GetHashCode()
    {
        int code = 0;
        foreach (var item in InstalledSdkVersions)
        {
            code = HashCode.Combine(code, item);
        }
        return code;
    }
}