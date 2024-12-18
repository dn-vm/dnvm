
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using Spectre.Console;
using StaticCs;
using Zio;
using RollForwardOptions =  Dnvm.GlobalJsonSubset.SdkSubset.RollForwardOptions;

namespace Dnvm;

[GenerateDeserialize]
internal sealed partial record GlobalJsonSubset
{
    public SdkSubset? Sdk { get; init; }

    [GenerateDeserialize]
    public sealed partial record SdkSubset
    {
        [SerdeMemberOptions(DeserializeProxy = typeof(NullableRefProxy.Deserialize<SemVersion, SemVersionProxy>))]
        public SemVersion? Version { get; init; }
        public RollForwardOptions? RollForward { get; init; }
        public bool? AllowPrerelease { get; init; }

        [GenerateDeserialize]
        [Closed]
        public enum RollForwardOptions
        {
            /// <summary>
            /// Uses the specified version.
            ///
            /// If not found, rolls forward to the latest patch level.
            ///
            /// If not found, fails.
            /// </summary>
            Patch,
            /// <summary>
            /// Uses the latest patch level for the specified major, minor, and feature band.
            ///
            /// If not found, rolls forward to the next higher feature band within the same
            /// major/minor and uses the latest patch level for that feature band.
            ///
            /// If not found, fails.
            /// </summary>
            Feature,
            /// <summary>
            /// Uses the latest patch level for the specified major, minor, and feature band.
            ///
            /// If not found, rolls forward to the next higher feature band within the same
            /// major/minor version and uses the latest patch level for that feature band.
            ///
            /// If not found, rolls forward to the next higher minor and feature band within the
            /// same major and uses the latest patch level for that feature band.
            ///
            /// If not found, fails.
            /// </summary>
            Minor,
            /// <summary>
            /// Uses the latest patch level for the specified major, minor, and feature band.
            ///
            /// If not found, rolls forward to the next higher feature band within the same
            /// major/minor version and uses the latest patch level for that feature band.
            ///
            /// If not found, rolls forward to the next higher minor and feature band within the
            /// same major and uses the latest patch level for that feature band.
            ///
            /// If not found, rolls forward to the next higher major, minor, and feature band
            /// and uses the latest patch level for that feature band.
            ///
            /// If not found, fails.
            /// </summary>
            Major,
            /// <summary>
            /// Uses the latest installed patch level that matches the requested major, minor,
            /// and feature band with a patch level that's greater than or equal to the
            /// specified value.
            ///
            /// If not found, fails.
            /// </summary>
            LatestPatch,
            /// <summary>
            /// Uses the highest installed feature band and patch level that matches the
            /// requested major and minor with a feature band and patch level that's greater
            /// than or equal to the specified value.
            ///
            /// If not found, fails.
            /// </summary>
            LatestFeature,
            /// <summary>
            /// Uses the highest installed minor, feature band, and patch level that matches the
            /// requested major with a minor, feature band, and patch level that's greater than
            /// or equal to the specified value.
            ///
            /// If not found, fails.
            /// </summary>
            LatestMinor,
            /// <summary>
            /// Uses the highest installed .NET SDK with a version that's greater than or equal
            /// to the specified value.
            ///
            /// If not found, fail.
            /// </summary>
            LatestMajor,
            /// <summary>
            /// Doesn't roll forward. An exact match is required.
            /// </summary>
            Disable,
        }
    }
}

public static partial class RestoreCommand
{
    public enum Error
    {
        NoGlobalJson = 1,
        IoError = 2,
        NoSdkSection = 3,
        NoVersion = 4,
        CouldntFetchReleaseIndex = 5,
        CantFindRequestedVersion = 6,
    }

    public static async Task<Result<SemVersion, Error>> Run(DnvmEnv env, Logger logger)
    {
        UPath? globalJsonPathOpt = null;
        UPath cwd = env.Cwd;
        while (true)
        {
            var testPath = cwd / "global.json";
            if (env.CwdFs.FileExists(testPath))
            {
                globalJsonPathOpt = testPath;
                break;
            }
            if (cwd == UPath.Root)
            {
                break;
            }
            cwd = cwd.GetDirectory();
        }

        if (globalJsonPathOpt is not {} globalJsonPath)
        {
            logger.Error("No global.json found in the current directory or any of its parents.");
            return Error.NoGlobalJson;
        }

        GlobalJsonSubset json;
        try
        {
            var text = env.CwdFs.ReadAllText(globalJsonPath);
            json = JsonSerializer.Deserialize<GlobalJsonSubset>(text);
        }
        catch (IOException e)
        {
            logger.Error("Failed to read global.json: " + e.Message);
            return Error.IoError;
        }

        if (json.Sdk is not {} sdk)
        {
            logger.Error("global.json does not contain an SDK section.");
            return Error.NoSdkSection;
        }

        if (sdk.Version is not {} version)
        {
            logger.Error("SDK section in global.json does not contain a version.");
            return Error.NoVersion;
        }

        var rollForward = sdk.RollForward ?? GlobalJsonSubset.SdkSubset.RollForwardOptions.LatestPatch;
        var allowPrerelease = sdk.AllowPrerelease ?? true;

        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await DotnetReleasesIndex.FetchLatestIndex(env.DotnetFeedUrls);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.Error("Could not fetch the releases index: ");
            logger.Error(e.Message);
            return Error.CouldntFetchReleaseIndex;
        }

        // Find the best SDK to match the roll-forward policy.

        var installDir = globalJsonPath.GetDirectory() / ".dotnet";

        var sdks = await GetSortedSdks(versionIndex, allowPrerelease);

        ChannelReleaseIndex.Component? Search(Comparison<SemVersion> comparer, bool preferExact)
        {
            if (preferExact && BinarySearchLatest(sdks, version, SemVersion.CompareSortOrder) is { } exactMatch)
            {
                return exactMatch;
            }
            return BinarySearchLatest(sdks, version, comparer);
        }

        ChannelReleaseIndex.Component? component = rollForward switch
        {
            RollForwardOptions.Patch => Search(LatestPatchComparison, preferExact: true),
            RollForwardOptions.Feature => Search(LatestFeatureComparison, preferExact: true),
            RollForwardOptions.Minor => Search(LatestMinorComparison, preferExact: true),
            RollForwardOptions.Major => Search(LatestMajorComparison, preferExact: true),
            RollForwardOptions.LatestPatch => Search(LatestPatchComparison, preferExact: false),
            RollForwardOptions.LatestFeature => Search(LatestFeatureComparison, preferExact: false),
            RollForwardOptions.LatestMinor => Search(LatestMinorComparison, preferExact: false),
            RollForwardOptions.LatestMajor => Search(LatestMajorComparison, preferExact: false),
            RollForwardOptions.Disable => Search(SemVersion.CompareSortOrder, preferExact: false),
        };

        if (component is null)
        {
            logger.Error("No SDK found that matches the requested version.");
            return Error.CantFindRequestedVersion;
        }

        var downloadUrl = component.Files.Single(f => f.Rid == Utilities.CurrentRID.ToString()).Url;

        var error = await InstallCommand.InstallSdkToDir(downloadUrl, env.CwdFs, installDir, env.TempFs, logger);
        if (error is not null)
        {
            return Error.IoError;
        }

        return component.Version;
    }

    /// <summary>
    /// Compares equal if the major, minor, and feature band versions are equal, and the patch
    /// version is greater or equal.
    /// </summary>
    private static int LatestPatchComparison(SemVersion a, SemVersion b)
    {
        if (a.Major != b.Major)
        {
            return a.Major.CompareTo(b.Major);
        }
        if (a.Minor != b.Minor)
        {
            return a.Minor.CompareTo(b.Minor);
        }
        // This is where dotnet differs from semver. The semver patch version is the latest
        // number in the version string, but dotnet expects SDKs versions to end in xnn, where
        // x is the feature band and nn is the patch level.
        if ((a.Patch / 100) != (b.Patch / 100))
        {
            return (a.Patch / 100).CompareTo(b.Patch / 100);
        }
        // If the patch version is >= we will consider the versions 'equal', meaning that they are
        // compatible.
        return ((a.Patch % 100) >= (b.Patch % 100)) ? 0 : -1;
    }

    /// <summary>
    /// Compares equal if the major and minor versions are equal, and the feature band and patch
    /// version are greater or equal.
    /// </summary>
    private static int LatestFeatureComparison(SemVersion a, SemVersion b)
    {
        if (a.Major != b.Major)
        {
            return a.Major.CompareTo(b.Major);
        }
        if (a.Minor != b.Minor)
        {
            return a.Minor.CompareTo(b.Minor);
        }
        if (a.Patch != b.Patch)
        {
            return a.Patch >= b.Patch ? 0 : -1;
        }
        return 0;
    }

    /// <summary>
    /// Compares equal if the major versions are equal, and the minor versions are greater or equal.
    /// </summary>
    private static int LatestMinorComparison(SemVersion a, SemVersion b)
    {
        if (a.Major != b.Major)
        {
            return a.Major.CompareTo(b.Major);
        }
        if (a.Minor != b.Minor)
        {
            return a.Minor >= b.Minor ? 0 : -1;
        }
        if (a.Patch != b.Patch)
        {
            return a.Patch >= b.Patch ? 0 : -1;
        }
        return 0;
    }

    /// <summary>
    /// Compares equal if the major versions are greater than or equal.
    /// </summary>
    private static int LatestMajorComparison(SemVersion a, SemVersion b)
    {
        if (a.Major != b.Major)
        {
            return a.Major >= b.Major ? 0 : -1;
        }
        if (a.Minor != b.Minor)
        {
            return a.Minor >= b.Minor ? 0 : -1;
        }
        if (a.Patch != b.Patch)
        {
            return a.Patch >= b.Patch ? 0 : -1;
        }
        return 0;
    }

    /// <summary>
    /// Finds the component with the given version. If multiple components have the same version,
    /// the one with the newest version is returned.
    /// </summary>
    /// <param name="sdks">A list of the components in descending sort order.</param>
    /// <param name="version">Version of the component to find.</param>
    /// <param name="comparer">Custom comparison for versions.</param>
    /// <returns>The component with the newest matching version, or null if no matching version is found.</returns>
    private static ChannelReleaseIndex.Component? BinarySearchLatest(
        List<ChannelReleaseIndex.Component> sdks,
        SemVersion version,
        Comparison<SemVersion> comparer)
    {
        // Note: the list is sorted in descending order.
        int left = 0;
        int right = sdks.Count;
        while (left < right)
        {
            int mid = left + (right - left) / 2;
            if (comparer(sdks[mid].Version, version) > 0)
            {
                left = mid + 1;
            }
            else
            {
                right = mid;
            }
        }
        return left < sdks.Count && comparer(sdks[left].Version, version) == 0 ? sdks[left] : null;
    }

    /// <summary>
    /// Returns a sorted (descending) list of SDks. If <paramref name="majorMinorVersion"/> is not
    /// null, only SDKs for that major+minor version are returned.
    /// </summary>
    private static async Task<List<ChannelReleaseIndex.Component>> GetSortedSdks(
        DotnetReleasesIndex versionIndex,
        bool allowPrerelease,
        string? majorMinorVersion = null)
    {
        var sdks = new List<ChannelReleaseIndex.Component>();
        foreach (var releaseIndex in versionIndex.ChannelIndices)
        {
            if (majorMinorVersion is not null && majorMinorVersion != releaseIndex.MajorMinorVersion)
            {
                continue;
            }
            var index = JsonSerializer.Deserialize<ChannelReleaseIndex>(await Program.HttpClient.GetStringAsync(releaseIndex.ChannelReleaseIndexUrl));
            foreach (var release in index.Releases)
            {
                foreach (var sdk in release.Sdks)
                {
                    if (allowPrerelease || !sdk.Version.IsPrerelease)
                    {
                        sdks.Add(sdk);
                    }
                }
            }
        }
        // Sort in descending order.
        sdks.Sort((a, b) => b.Version.CompareSortOrderTo(a.Version));
        return sdks;
    }
}