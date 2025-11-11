
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde.Json;
using Spectre.Console;

namespace Dnvm;

public static class ListRemoteCommand
{
    public static async Task<int> Run(DnvmEnv env, DnvmSubCommand.ListRemoteArgs args)
    {
        var console = env.Console;
        var feedUrls = args.FeedUrl is not null ? new[] { args.FeedUrl } : env.DotnetFeedUrls;

        DotnetReleasesIndex releasesIndex;
        try
        {
            releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, feedUrls);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            console.Error($"Could not fetch the releases index: {e.Message}");
            return 1;
        }

        var supportedChannels = GetSupportedChannels(releasesIndex);
        var sdkVersions = await GetLatestSdkVersionsByFeature(env, releasesIndex, supportedChannels);

        PrintRemoteSdks(console, sdkVersions);

        return 0;
    }

    private static List<DotnetReleasesIndex.ChannelIndex> GetSupportedChannels(DotnetReleasesIndex releasesIndex)
    {
        return releasesIndex.ChannelIndices
            .Where(c => c.SupportPhase.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                       c.SupportPhase.Equals("go-live", StringComparison.OrdinalIgnoreCase) ||
                       c.SupportPhase.Equals("preview", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => SemVersion.Parse(c.LatestRelease, SemVersionStyles.Strict))
            .ToList();
    }

    private static async Task<List<SdkVersionInfo>> GetLatestSdkVersionsByFeature(
        DnvmEnv env,
        DotnetReleasesIndex releasesIndex,
        List<DotnetReleasesIndex.ChannelIndex> supportedChannels)
    {
        var result = new List<SdkVersionInfo>();
        
        foreach (var channelIndex in supportedChannels)
        {
            ChannelReleaseIndex releaseIndex;
            try
            {
                var releaseIndexText = await env.HttpClient.GetStringAsync(channelIndex.ChannelReleaseIndexUrl);
                releaseIndex = JsonSerializer.Deserialize<ChannelReleaseIndex>(releaseIndexText);
            }
            catch (Exception)
            {
                // Skip channels that fail to fetch
                continue;
            }

            // Group SDKs by feature version (major.minor.featureBand)
            var sdksByFeature = new Dictionary<string, SdkVersionInfo>();
            
            foreach (var release in releaseIndex.Releases)
            {
                foreach (var sdk in release.Sdks)
                {
                    var featureVersion = sdk.Version.ToFeature();
                    
                    if (!sdksByFeature.ContainsKey(featureVersion) ||
                        SemVersion.ComparePrecedence(sdk.Version, sdksByFeature[featureVersion].Version) > 0)
                    {
                        sdksByFeature[featureVersion] = new SdkVersionInfo
                        {
                            Version = sdk.Version,
                            FeatureVersion = featureVersion,
                            MajorMinor = channelIndex.MajorMinorVersion,
                            ReleaseType = channelIndex.ReleaseType,
                            SupportPhase = channelIndex.SupportPhase
                        };
                    }
                }
            }

            result.AddRange(sdksByFeature.Values);
        }

        return result.OrderByDescending(s => s.Version).ToList();
    }

    private static void PrintRemoteSdks(IAnsiConsole console, List<SdkVersionInfo> sdkVersions)
    {
        console.WriteLine("Available SDK versions (latest patch for each feature version):");
        console.WriteLine();

        var table = new Table();
        table.AddColumn("Version");
        table.AddColumn("Feature");
        table.AddColumn("Channel");
        table.AddColumn("Support");

        foreach (var sdk in sdkVersions)
        {
            var supportText = sdk.ReleaseType.ToUpperInvariant();
            table.AddRow(
                sdk.Version.ToString(),
                sdk.FeatureVersion,
                sdk.MajorMinor,
                supportText);
        }

        console.Write(table);
    }

    private record SdkVersionInfo
    {
        public required SemVersion Version { get; init; }
        public required string FeatureVersion { get; init; }
        public required string MajorMinor { get; init; }
        public required string ReleaseType { get; init; }
        public required string SupportPhase { get; init; }
    }
}
