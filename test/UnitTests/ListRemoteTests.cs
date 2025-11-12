using Semver;
using Spectre.Console.Testing;
using Xunit;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class ListRemoteTests
{
    // Tests for data correctness (filtering, sorting, selection logic)
    [Fact]
    public Task GetRemoteSdkVersions_SelectsLatestPatchForEachFeatureBand() => RunWithServer(async (server, env) =>
    {
        // Register multiple SDK versions in different feature bands
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(8, 0, 101), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(8, 0, 200), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(8, 0, 201), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(9, 0, 100), "sts", "active");
        server.RegisterReleaseVersion(new SemVersion(9, 0, 101), "sts", "active");

        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, new[] { server.PrefixString });
        var sdkVersions = await ListRemoteCommand.GetRemoteSdkVersions(env, releasesIndex);

        // Create expected list with only latest patch versions for each feature band
        var expected = new List<ListRemoteCommand.SdkVersionInfo>
        {
            new() { Version = new SemVersion(9, 0, 101), FeatureVersion = "9.0.1xx", MajorMinor = "9.0", ReleaseType = "sts", SupportPhase = "active" },
            new() { Version = new SemVersion(8, 0, 201), FeatureVersion = "8.0.2xx", MajorMinor = "8.0", ReleaseType = "lts", SupportPhase = "active" },
            new() { Version = new SemVersion(8, 0, 101), FeatureVersion = "8.0.1xx", MajorMinor = "8.0", ReleaseType = "lts", SupportPhase = "active" },
        };

        Assert.Equal(expected, sdkVersions);
    });

    [Fact]
    public Task GetRemoteSdkVersions_FiltersUnsupportedVersions() => RunWithServer(async (server, env) =>
    {
        // Register versions with different support phases
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(7, 0, 100), "lts", "eol"); // End of life
        server.RegisterReleaseVersion(new SemVersion(9, 0, 100), "sts", "preview");

        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, new[] { server.PrefixString });
        var sdkVersions = await ListRemoteCommand.GetRemoteSdkVersions(env, releasesIndex);

        // Create expected list with only supported versions (active and preview, not eol)
        var expected = new List<ListRemoteCommand.SdkVersionInfo>
        {
            new() { Version = new SemVersion(9, 0, 100), FeatureVersion = "9.0.1xx", MajorMinor = "9.0", ReleaseType = "sts", SupportPhase = "preview" },
            new() { Version = new SemVersion(8, 0, 100), FeatureVersion = "8.0.1xx", MajorMinor = "8.0", ReleaseType = "lts", SupportPhase = "active" },
        };

        Assert.Equal(expected, sdkVersions);
    });

    [Fact]
    public Task GetRemoteSdkVersions_SortsByVersionDescending() => RunWithServer(async (server, env) =>
    {
        // Register versions in random order
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(10, 0, 100), "lts", "preview");
        server.RegisterReleaseVersion(new SemVersion(9, 0, 100), "sts", "active");

        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, new[] { server.PrefixString });
        var sdkVersions = await ListRemoteCommand.GetRemoteSdkVersions(env, releasesIndex);

        // Create expected list sorted by version descending
        var expected = new List<ListRemoteCommand.SdkVersionInfo>
        {
            new() { Version = new SemVersion(10, 0, 100), FeatureVersion = "10.0.1xx", MajorMinor = "10.0", ReleaseType = "lts", SupportPhase = "preview" },
            new() { Version = new SemVersion(9, 0, 100), FeatureVersion = "9.0.1xx", MajorMinor = "9.0", ReleaseType = "sts", SupportPhase = "active" },
            new() { Version = new SemVersion(8, 0, 100), FeatureVersion = "8.0.1xx", MajorMinor = "8.0", ReleaseType = "lts", SupportPhase = "active" },
        };

        Assert.Equal(expected, sdkVersions);
    });

    [Fact]
    public Task GetRemoteSdkVersions_IncludesMetadata() => RunWithServer(async (server, env) =>
    {
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");

        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, new[] { server.PrefixString });
        var sdkVersions = await ListRemoteCommand.GetRemoteSdkVersions(env, releasesIndex);

        // Create expected list with all metadata fields
        var expected = new List<ListRemoteCommand.SdkVersionInfo>
        {
            new() { Version = new SemVersion(8, 0, 100), FeatureVersion = "8.0.1xx", MajorMinor = "8.0", ReleaseType = "lts", SupportPhase = "active" },
        };

        Assert.Equal(expected, sdkVersions);
    });

    // Tests for output formatting
    [Fact]
    public Task ListRemote_OutputContainsTableHeaders() => RunWithServer(async (server, env) =>
    {
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");

        var args = new DnvmSubCommand.ListRemoteArgs { FeedUrl = null };
        var result = await ListRemoteCommand.Run(env, args);
        Assert.Equal(0, result);

        var console = (TestConsole)env.Console;
        var output = console.Output;
        
        // Verify the table headers are present
        Assert.Contains("Available SDK versions", output);
        Assert.Contains("Version", output);
        Assert.Contains("Feature", output);
        Assert.Contains("Channel", output);
        Assert.Contains("Support", output);
    });

    [Fact]
    public Task ListRemote_OutputContainsVersionData() => RunWithServer(async (server, env) =>
    {
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");

        var args = new DnvmSubCommand.ListRemoteArgs { FeedUrl = null };
        var result = await ListRemoteCommand.Run(env, args);
        Assert.Equal(0, result);

        var console = (TestConsole)env.Console;
        var output = console.Output;

        // Verify version data appears in output
        Assert.Contains("8.0.100", output);
        Assert.Contains("8.0.1xx", output);
        Assert.Contains("LTS", output);
    });

    [Fact]
    public Task ListRemote_WorksWithCustomFeedUrl() => RunWithServer(async (server, env) =>
    {
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");

        var args = new DnvmSubCommand.ListRemoteArgs { FeedUrl = server.PrefixString };
        var result = await ListRemoteCommand.Run(env, args);
        Assert.Equal(0, result);

        var console = (TestConsole)env.Console;
        var output = console.Output;

        Assert.Contains("8.0.100", output);
        Assert.Contains("8.0.1xx", output);
    });
}
