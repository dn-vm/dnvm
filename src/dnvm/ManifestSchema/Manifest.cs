
using System;
using System.Collections.Generic;
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
public partial record TrackedChannel
{
    public required Channel ChannelName { get; init; }
    public required SdkDirName SdkDirName { get; init; }
    [SerdeMemberOptions(
        WrapperSerialize = typeof(EqArraySerdeWrap.SerializeImpl<SemVersion, SemVersionSerdeWrap>),
        WrapperDeserialize = typeof(EqArraySerdeWrap.DeserializeImpl<SemVersion, SemVersionSerdeWrap>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
}

[GenerateSerde]
public partial record InstalledSdk
{
    [SerdeWrap(typeof(SemVersionSerdeWrap))]
    public required SemVersion ReleaseVersion { get; init; }
    [SerdeWrap(typeof(SemVersionSerdeWrap))]
    public required SemVersion SdkVersion { get; init; }
    [SerdeWrap(typeof(SemVersionSerdeWrap))]
    public required SemVersion RuntimeVersion { get; init; }
    [SerdeWrap(typeof(SemVersionSerdeWrap))]
    public required SemVersion AspNetVersion { get; init; }

    public SdkDirName SdkDirName { get; init; } = DnvmEnv.DefaultSdkDirName;

    /// <summary>
    /// Indicates which channel this SDK was installed from, if any.
    /// </summary>
    public Channel? Channel { get; init; } = null;
}

public static partial class ManifestConvert
{
    public static async Task<Manifest> Convert(this ManifestV3 v3, DotnetReleasesIndex releasesIndex)
    {
        var channelMemo = new SortedDictionary<SemVersion, ChannelReleaseIndex>(SemVersion.SortOrderComparer);

        var getChannelIndex = async (SemVersion majorMinor) =>
        {
            if (channelMemo.TryGetValue(majorMinor, out var channelReleaseIndex))
            {
                return channelReleaseIndex;
            }

            var channelRelease = releasesIndex.Releases.Single(r => r.MajorMinorVersion == majorMinor.ToString());
            channelReleaseIndex = JsonSerializer.Deserialize<ChannelReleaseIndex>(
                await Program.HttpClient.GetStringAsync(channelRelease.ChannelReleaseIndexUrl));
            channelMemo[majorMinor] = channelReleaseIndex;
            return channelReleaseIndex;
        };

        return new Manifest
        {
            InstalledSdkVersions = (await v3.InstalledSdkVersions.SelectAsArray(v => v.Convert(v3, getChannelIndex))).ToEq(),
            TrackedChannels = v3.TrackedChannels.SelectAsArray(c => c.Convert()).ToEq(),
        };
    }

    public static async Task<InstalledSdk> Convert(
        this InstalledSdkV3 v3,
        ManifestV3 manifestV3,
        Func<SemVersion, Task<ChannelReleaseIndex>> getChannelIndex)
    {
        // Take the major and minor version from the installed SDK and use it to find the corresponding
        // version in the releases index. Then grab the component versions from that release and fill
        // in the remaining sections in the InstalledSdk
        var v3Version = SemVersion.Parse(v3.Version, SemVersionStyles.Strict);
        var majorMinorVersion = new SemVersion(v3Version.Major, v3Version.Minor);

        var channelReleaseIndex = await getChannelIndex(majorMinorVersion);
        var exactRelease = channelReleaseIndex.Releases.Single(r => r.Sdk.Version == v3Version);

        Channel? channel = manifestV3.TrackedChannels
            .SingleOrNull(c => c.SdkDirName == v3.SdkDirName)?.ChannelName;

        return new InstalledSdk()
        {
            ReleaseVersion = exactRelease.ReleaseVersion,
            SdkVersion = exactRelease.Sdk.Version,
            RuntimeVersion = exactRelease.Runtime.Version,
            AspNetVersion = exactRelease.AspNetCore.Version,
            SdkDirName = v3.SdkDirName,
            Channel = channel
        };
    }

    public static TrackedChannel Convert(this TrackedChannelV3 v3) => new TrackedChannel {
        ChannelName = v3.ChannelName,
        SdkDirName = v3.SdkDirName,
        InstalledSdkVersions = v3.InstalledSdkVersions.Select(v => SemVersion.Parse(v, SemVersionStyles.Strict)).ToEq(),
    };
}