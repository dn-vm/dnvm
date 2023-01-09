
using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Schema;
using Semver;
using Serde;
using Serde.Json;

namespace Dnvm;

public static class VersionInfoClient
{
    public const string ReleasesUrlSuffix = "/release-metadata/releases-index.json";
    public async static Task<DotnetReleasesIndex> FetchLatestIndex(string feed, string urlSuffix = ReleasesUrlSuffix)
    {
        var response = await Program.HttpClient.GetStringAsync(feed + urlSuffix);
        return JsonSerializer.Deserialize<DotnetReleasesIndex>(response);
    }

    public static DotnetReleasesIndex.Release? GetLatestReleaseForChannel(this DotnetReleasesIndex index, Channel c)
    {
        (DotnetReleasesIndex.Release Release, SemVersion Version)? latestRelease = null;
        foreach (var release in index.Releases)
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
                case Channel.Preview when supportPhase is "active" or "preview":
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
        public required string ChannelVersion { get; init; }
        public required string LatestRelease { get; init; }
        public required string ReleaseType { get; init; }
        public required string SupportPhase { get; init; }
        public required string LatestSdk { get; init; }
    }
}
