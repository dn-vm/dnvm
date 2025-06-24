
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;

namespace Dnvm;

/// <summary>
/// Holds the simple name of a directory that contains one or more SDKs and lives under DNVM_HOME.
/// This is a wrapper to prevent being used directly as a path.
/// </summary>
[GenerateSerde]
public sealed partial record SdkDirNameV5(string Name)
{
    public string Name { get; init; } = Name.ToLower();
    public static implicit operator SdkDirNameV5(SdkDirNameV4 dirName) => new SdkDirNameV5(dirName.Name);
}

[GenerateSerde]
public sealed partial record ManifestV5
{
    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 5;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;

    public required SdkDirNameV5 CurrentSdkDir { get; init; }
    public EqArray<InstalledSdkV5> InstalledSdkVersions { get; init; } = [];
    public EqArray<TrackedChannelV5> TrackedChannels { get; init; } = [];

    internal ManifestV5 Untrack(Channel channel)
    {
        return this with
        {
            TrackedChannels = TrackedChannels.Where(c => c.ChannelName != channel).ToEq()
        };
    }
}

[GenerateSerde]
public partial record TrackedChannelV5
{
    public required Channel ChannelName { get; init; }
    public required SdkDirNameV5 SdkDirName { get; init; }
    [SerdeMemberOptions(
        SerializeProxy = typeof(EqArrayProxy.Ser<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(EqArrayProxy.De<SemVersion, SemVersionProxy>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
}

[GenerateSerde]
public partial record InstalledSdkV5
{
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion ReleaseVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion SdkVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion RuntimeVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion AspNetVersion { get; init; }
    public required SdkDirNameV5 SdkDirName { get; init; }

    /// <summary>
    /// Indicates which channel this SDK was installed from, if any.
    /// </summary>
    public Channel? Channel { get; init; } = null;
}

public static partial class ManifestV5Convert
{
    public static async Task<ManifestV5> Convert(this ManifestV4 v4, ScopedHttpClient httpClient, DotnetReleasesIndex releasesIndex)
    {
        var channelMemo = new SortedDictionary<SemVersion, ChannelReleaseIndex>(SemVersion.SortOrderComparer);

        var getChannelIndex = async (SemVersion majorMinor) =>
        {
            if (channelMemo.TryGetValue(majorMinor, out var channelReleaseIndex))
            {
                return channelReleaseIndex;
            }

            var channelRelease = releasesIndex.ChannelIndices.Single(r => r.MajorMinorVersion == majorMinor.ToMajorMinor());
            channelReleaseIndex = JsonSerializer.Deserialize<ChannelReleaseIndex>(
                await httpClient.GetStringAsync(channelRelease.ChannelReleaseIndexUrl));
            channelMemo[majorMinor] = channelReleaseIndex;
            return channelReleaseIndex;
        };

        return new ManifestV5
        {
            InstalledSdkVersions = (await v4.InstalledSdkVersions.SelectAsArray(v => v.Convert(v4, getChannelIndex))).ToEq(),
            TrackedChannels = v4.TrackedChannels.SelectAsArray(c => c.Convert()).ToEq(),
            CurrentSdkDir = v4.CurrentSdkDir,
        };
    }

    public static async Task<InstalledSdkV5> Convert(
        this InstalledSdkV4 v4,
        ManifestV4 manifestV4,
        Func<SemVersion, Task<ChannelReleaseIndex>> getChannelIndex)
    {
        // Take the major and minor version from the installed SDK and use it to find the corresponding
        // version in the releases index. Then grab the component versions from that release and fill
        // in the remaining sections in the InstalledSdkV5
        var v4Version = SemVersion.Parse(v4.Version, SemVersionStyles.Strict);
        var majorMinorVersion = new SemVersion(v4Version.Major, v4Version.Minor);

        var channelReleaseIndex = await getChannelIndex(majorMinorVersion);
        var exactRelease = channelReleaseIndex.Releases
            .Where(r => r.Sdks.Any(s => s.Version == v4Version))
            .Single();


        Channel? channel = (v4Version.Major, v4Version.Minor) switch {
            (6, 0) => new Channel.Lts(),
            (7, 0) => new Channel.Latest(),
            (8, 0) => new Channel.Preview(),
            _ => manifestV4.TrackedChannels
                 .SingleOrNull(c => c.SdkDirName == v4.SdkDirName)?.ChannelName
        } ;

        return new InstalledSdkV5()
        {
            ReleaseVersion = exactRelease.ReleaseVersion,
            SdkVersion = v4Version,
            RuntimeVersion = exactRelease.Runtime.Version,
            AspNetVersion = exactRelease.AspNetCore.Version,
            SdkDirName = v4.SdkDirName,
            Channel = channel
        };
    }

    public static TrackedChannelV5 Convert(this TrackedChannelV4 v3) => new TrackedChannelV5 {
        ChannelName = v3.ChannelName,
        SdkDirName = v3.SdkDirName,
        InstalledSdkVersions = v3.InstalledSdkVersions.Select(v => SemVersion.Parse(v, SemVersionStyles.Strict)).ToEq(),
    };
}