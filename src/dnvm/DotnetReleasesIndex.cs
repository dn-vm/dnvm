
using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Schema;
using Semver;
using Serde;
using Serde.Json;

namespace Dnvm;

public partial record DotnetReleasesIndex
{
    public const string ReleasesUrlSuffix = "/release-metadata/releases-index.json";
    public async static Task<DotnetReleasesIndex> FetchLatestIndex(string feed, string urlSuffix = ReleasesUrlSuffix)
    {
        var response = await Program.HttpClient.GetStringAsync(feed.TrimEnd('/') + urlSuffix);
        return JsonSerializer.Deserialize<DotnetReleasesIndex>(response);
    }

    public ChannelIndex? GetChannelIndex(Channel c)
    {
        (ChannelIndex Release, SemVersion Version)? latestRelease = null;
        foreach (var release in this.Releases)
        {
            var supportPhase = release.SupportPhase.ToLowerInvariant();
            var releaseType = release.ReleaseType.ToLowerInvariant();
            if (!SemVersion.TryParse(release.LatestRelease, SemVersionStyles.Strict, out var releaseVersion))
            {
                continue;
            }
            var found = (c, supportPhase, releaseType) switch
            {
                (Channel.Latest, "active", _)
                or (Channel.Lts, "active", "lts")
                or (Channel.Sts, "active", "sts")
                or (Channel.Preview, "go-live", _)
                or (Channel.Preview, "preview", _) => true,
                (Channel.Versioned v, _, _) when v.ToString() == releaseVersion.ToMajorMinor() => true,
                _ => false
            };
            if (found &&
                (latestRelease is not { } latest ||
                 SemVersion.ComparePrecedence(releaseVersion, latest.Version) > 0))
            {
                latestRelease = (release, releaseVersion);
            }
        }
        return latestRelease?.Release;
    }
}

[GenerateSerde]
public partial record DotnetReleasesIndex
{
    [SerdeMemberOptions(Rename = "releases-index")]
    public required ImmutableArray<ChannelIndex> Releases { get; init; }

    [GenerateSerde]
    [SerdeTypeOptions(MemberFormat = MemberFormat.KebabCase)]
    public partial record ChannelIndex
    {
        /// <summary>
        /// The major and minor version of the release, e.g. '42.42'.
        /// </summary>
        [SerdeMemberOptions(Rename = "channel-version")]
        public required string MajorMinorVersion { get; init; }
        /// <summary>
        /// The version number of the latest SDK, e.g. '42.42.104'.
        /// </summary>
        public required string LatestSdk { get; init; }
        /// <summary>
        /// The version number of the release, e.g. '42.42.4'.
        /// </summary>
        public required string LatestRelease { get; init; }
        /// <summary>
        /// Whether this version is in an LTS or STS cadence.
        /// </summary>
        public required string ReleaseType { get; init; }
        /// <summary>
        /// The support phase the release is in, e.g. 'active' or 'eol'.
        /// </summary>
        public required string SupportPhase { get; init; }
        /// <summary>
        /// The URL to the releases index for this channel.
        /// </summary>
        [SerdeMemberOptions(Rename = "releases.json")]
        public required string ChannelReleaseIndexUrl { get; init; }
    }
}

[GenerateSerde]
public partial record ChannelReleaseIndex
{
    public static Release CreateRelease(SemVersion universalVersion)
    {
        var component = new Component { Version = universalVersion };
        return new() {
            ReleaseVersion = universalVersion,
            Runtime = component,
            Sdk = component,
            Sdks = [ component ],
            AspNetCore = component,
            WindowsDesktop = component
        };
    }

    public required EqArray<Release> Releases { get; init; }

    [GenerateSerde]
    [SerdeTypeOptions(MemberFormat = MemberFormat.KebabCase)]
    public partial record Release()
    {
        [SerdeWrap(typeof(SemVersionSerdeWrap))]
        public required SemVersion ReleaseVersion { get; init; }
        public required Component Runtime { get; init; }
        public required Component Sdk { get; init; }
        public required EqArray<Component> Sdks { get; init; }
        [SerdeMemberOptions(Rename = "aspnetcore-runtime")]
        public required Component AspNetCore { get; init; }
        [SerdeMemberOptions(Rename = "windowsdesktop")]
        public required Component WindowsDesktop { get; init; }
    }

    [GenerateSerde]
    [SerdeTypeOptions(MemberFormat = MemberFormat.KebabCase)]
    public partial record Component
    {
        [SerdeWrap(typeof(SemVersionSerdeWrap))]
        public required SemVersion Version { get; init; }
    }
}