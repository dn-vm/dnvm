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

        // Verify only latest patch versions are selected for each feature band
        Assert.Contains(sdkVersions, s => s.Version == new SemVersion(9, 0, 101)); // Latest in 9.0.1xx
        Assert.DoesNotContain(sdkVersions, s => s.Version == new SemVersion(9, 0, 100)); // Not the latest

        Assert.Contains(sdkVersions, s => s.Version == new SemVersion(8, 0, 201)); // Latest in 8.0.2xx
        Assert.DoesNotContain(sdkVersions, s => s.Version == new SemVersion(8, 0, 200)); // Not the latest

        Assert.Contains(sdkVersions, s => s.Version == new SemVersion(8, 0, 101)); // Latest in 8.0.1xx
        Assert.DoesNotContain(sdkVersions, s => s.Version == new SemVersion(8, 0, 100)); // Not the latest

        // Verify feature version is correctly set
        var sdk901 = sdkVersions.First(s => s.Version == new SemVersion(9, 0, 101));
        Assert.Equal("9.0.1xx", sdk901.FeatureVersion);
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

        // Verify active and preview versions are included
        Assert.Contains(sdkVersions, s => s.Version == new SemVersion(8, 0, 100)); // Active
        Assert.Contains(sdkVersions, s => s.Version == new SemVersion(9, 0, 100)); // Preview

        // Verify EOL versions are filtered out
        Assert.DoesNotContain(sdkVersions, s => s.Version == new SemVersion(7, 0, 100));
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

        // Verify they are sorted in descending order
        Assert.Equal(3, sdkVersions.Count);
        Assert.Equal(new SemVersion(10, 0, 100), sdkVersions[0].Version);
        Assert.Equal(new SemVersion(9, 0, 100), sdkVersions[1].Version);
        Assert.Equal(new SemVersion(8, 0, 100), sdkVersions[2].Version);
    });

    [Fact]
    public Task GetRemoteSdkVersions_IncludesMetadata() => RunWithServer(async (server, env) =>
    {
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");

        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(env.HttpClient, new[] { server.PrefixString });
        var sdkVersions = await ListRemoteCommand.GetRemoteSdkVersions(env, releasesIndex);

        // Verify metadata is correctly populated
        var sdk = sdkVersions.First();
        Assert.Equal(new SemVersion(8, 0, 100), sdk.Version);
        Assert.Equal("8.0.1xx", sdk.FeatureVersion);
        Assert.Equal("8.0", sdk.MajorMinor);
        Assert.Equal("lts", sdk.ReleaseType);
        Assert.Equal("active", sdk.SupportPhase);
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
