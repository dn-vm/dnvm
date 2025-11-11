using Semver;
using Spectre.Console.Testing;
using Xunit;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class ListRemoteTests
{
    [Fact]
    public Task ListRemoteBasic() => RunWithServer(async (server, env) =>
    {
        // Register multiple SDK versions in different feature bands
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(8, 0, 101), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(8, 0, 200), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(8, 0, 201), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(9, 0, 100), "sts", "active");
        server.RegisterReleaseVersion(new SemVersion(9, 0, 101), "sts", "active");

        var args = new DnvmSubCommand.ListRemoteArgs { FeedUrl = null };
        var result = await ListRemoteCommand.Run(env, args);
        Assert.Equal(0, result);

        var console = (TestConsole)env.Console;
        var output = console.Output;
        
        // Verify the table header is present
        Assert.Contains("Available SDK versions", output);
        Assert.Contains("Version", output);
        Assert.Contains("Feature", output);
        Assert.Contains("Channel", output);
        Assert.Contains("Support", output);

        // Verify only latest patch versions are shown for each feature band
        Assert.Contains("9.0.101", output); // Latest in 9.0.1xx
        Assert.Contains("9.0.1xx", output);
        Assert.DoesNotContain("9.0.100", output); // Not the latest in 9.0.1xx

        Assert.Contains("8.0.201", output); // Latest in 8.0.2xx
        Assert.Contains("8.0.2xx", output);
        Assert.DoesNotContain("8.0.200", output); // Not the latest in 8.0.2xx

        Assert.Contains("8.0.101", output); // Latest in 8.0.1xx
        Assert.Contains("8.0.1xx", output);
        Assert.DoesNotContain("8.0.100", output); // Not the latest in 8.0.1xx
    });

    [Fact]
    public Task ListRemoteFiltersUnsupportedVersions() => RunWithServer(async (server, env) =>
    {
        // Register versions with different support phases
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(7, 0, 100), "lts", "eol"); // End of life
        server.RegisterReleaseVersion(new SemVersion(9, 0, 100), "sts", "preview");

        var args = new DnvmSubCommand.ListRemoteArgs { FeedUrl = null };
        var result = await ListRemoteCommand.Run(env, args);
        Assert.Equal(0, result);

        var console = (TestConsole)env.Console;
        var output = console.Output;

        // Verify active and preview versions are shown
        Assert.Contains("8.0.100", output); // Active
        Assert.Contains("9.0.100", output); // Preview

        // Verify EOL versions are not shown
        Assert.DoesNotContain("7.0.100", output);
    });

    [Fact]
    public Task ListRemoteSortsByVersion() => RunWithServer(async (server, env) =>
    {
        // Register versions in random order
        server.ClearVersions();
        server.RegisterReleaseVersion(new SemVersion(8, 0, 100), "lts", "active");
        server.RegisterReleaseVersion(new SemVersion(10, 0, 100), "lts", "preview");
        server.RegisterReleaseVersion(new SemVersion(9, 0, 100), "sts", "active");

        var args = new DnvmSubCommand.ListRemoteArgs { FeedUrl = null };
        var result = await ListRemoteCommand.Run(env, args);
        Assert.Equal(0, result);

        var console = (TestConsole)env.Console;
        var lines = console.Lines.ToList();

        // Find the lines with version numbers
        var version10Line = lines.FindIndex(l => l.Contains("10.0.100"));
        var version9Line = lines.FindIndex(l => l.Contains("9.0.100"));
        var version8Line = lines.FindIndex(l => l.Contains("8.0.100"));

        // Verify they are sorted in descending order
        Assert.True(version10Line > 0 && version10Line < version9Line);
        Assert.True(version9Line < version8Line);
    });

    [Fact]
    public Task ListRemoteWithCustomFeedUrl() => RunWithServer(async (server, env) =>
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
