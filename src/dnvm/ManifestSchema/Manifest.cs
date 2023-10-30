
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using StaticCs;
using StaticCs.Collections;

namespace Dnvm;

[GenerateSerde]
public sealed partial record Manifest
{
    public static readonly Manifest Empty = new();

    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 4;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;

    public SdkDirName CurrentSdkDir { get; init; } = DnvmEnv.DefaultSdkDirName;
    public EqArray<InstalledSdk> InstalledSdkVersions { get; init; } = [];
    public EqArray<TrackedChannel> TrackedChannels { get; init; } = [];

    internal Manifest Untrack(Channel channel)
    {
        return this with
        {
            TrackedChannels = TrackedChannels.Where(c => c.ChannelName != channel).ToEq()
        };
    }
}

[GenerateSerde]
public readonly partial record struct TrackedChannel()
{
    public required Channel ChannelName { get; init; }
    public required SdkDirName SdkDirName { get; init; }
    [SerdeMemberOptions(
        WrapperSerialize = typeof(EqArraySerdeWrap.SerializeImpl<SemVersion, SemVersionSerdeWrap>),
        WrapperDeserialize = typeof(EqArraySerdeWrap.DeserializeImpl<SemVersion, SemVersionSerdeWrap>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
}

[GenerateSerde]
public readonly partial record struct InstalledSdk(
    [property: SerdeWrap(typeof(SemVersionSerdeWrap))]
    SemVersion Version)
{
    public SdkDirName SdkDirName { get; init; } = DnvmEnv.DefaultSdkDirName;

    /// <summary>
    /// Indicates which channel this SDK was installed from, if any.
    /// </summary>
    public Channel? Channel { get; init; } = null;
}

public static partial class ManifestConvert
{
    public static Manifest Convert(this ManifestV3 v3) => new Manifest
    {
        InstalledSdkVersions = v3.InstalledSdkVersions.SelectAsArray(v => v.Convert(v3)).ToEq(),
        TrackedChannels = v3.TrackedChannels.SelectAsArray(c => c.Convert()).ToEq(),
    };

    public static InstalledSdk Convert(this InstalledSdkV3 v3, ManifestV3 manifestV3)
    {
        Channel? channel = null;
        channel = manifestV3.TrackedChannels
            .SingleOrNull(c => c.SdkDirName == v3.SdkDirName)?.ChannelName;

        return new InstalledSdk(SemVersion.Parse(v3.Version, SemVersionStyles.Strict)) {
            SdkDirName = v3.SdkDirName, Channel = channel
        };
    }

    public static TrackedChannel Convert(this TrackedChannelV3 v3) => new TrackedChannel {
        ChannelName = v3.ChannelName,
        SdkDirName = v3.SdkDirName,
        InstalledSdkVersions = v3.InstalledSdkVersions.Select(v => SemVersion.Parse(v, SemVersionStyles.Strict)).ToEq(),
    };
}