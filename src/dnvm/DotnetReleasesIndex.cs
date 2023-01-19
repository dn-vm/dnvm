
using System;
using System.Collections.Immutable;
using System.Net.Http;
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
        var response = await Program.HttpClient.GetStringAsync(feed + urlSuffix);
        return JsonSerializer.Deserialize<DotnetReleasesIndex>(response);
    }

    public Release? GetLatestReleaseForChannel(Channel c)
    {
        (Release Release, SemVersion Version)? latestRelease = null;
        foreach (var release in this.Releases)
        {
            var supportPhase = release.SupportPhase.ToLowerInvariant();
            var releaseType = release.ReleaseType.ToLowerInvariant();
            if (!SemVersion.TryParse(release.LatestRelease, SemVersionStyles.Strict, out var releaseVersion))
            {
                continue;
            }
            switch (c)
            {
                case Channel.Latest when supportPhase is "active":
                case Channel.Lts when releaseType is "lts":
                case Channel.Sts when releaseType is "sts":
                //case Channel.Preview when supportPhase is "active" or "preview":
                    if (latestRelease is not { } latest ||
                        SemVersion.ComparePrecedence(releaseVersion, latest.Version) > 0)
                    {
                        latestRelease = (release, releaseVersion);
                    }
                   break;
            }
        }
        return latestRelease?.Release;
    }
}

[GenerateSerde]
public partial record DotnetReleasesIndex
{
    [SerdeMemberOptions(Rename = "releases-index")]
    public required ImmutableArray<Release> Releases { get; init; }

    [GenerateSerde]
    [SerdeTypeOptions(MemberFormat = MemberFormat.KebabCase)]
    public partial record Release
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
    }
}
