using Semver;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using Zio.FileSystems;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class ListTests
{
    private readonly Logger _logger;

    public ListTests()
    {
       _logger = new Logger(new StringWriter());
    }

    [Fact]
    public void BasicList()
    {
        var previewVersion = SemVersion.Parse("4.0.0-preview1", SemVersionStyles.Strict);
        var manifest = Manifest.Empty
            .AddSdk(new InstalledSdk() {
                SdkVersion = new(1,0,0),
                RuntimeVersion = new(1,0,0),
                AspNetVersion = new(1,0,0),
                ReleaseVersion = new(1,0,0) }, new Channel.Latest())
            .AddSdk(new InstalledSdk() {
                SdkVersion = previewVersion,
                RuntimeVersion = previewVersion,
                AspNetVersion = previewVersion,
                ReleaseVersion = previewVersion,
                SdkDirName = new("preview") }, new Channel.Preview());

        const string fakeHome = "/home";
        var console = new TestConsole();
        ListCommand.PrintSdks(console, manifest, fakeHome);
        var output = $"""
DNVM_HOME: {fakeHome}

Installed SDKs:

┌───┬────────────────┬─────────┬──────────┐
│   │ Version        │ Channel │ Location │
├───┼────────────────┼─────────┼──────────┤
│ * │ 1.0.0          │ latest  │ dn       │
│   │ 4.0.0-preview1 │ preview │ preview  │
└───┴────────────────┴─────────┴──────────┘

Tracked channels:

 • latest
 • preview
""";

        Assert.Equal(output, string.Join(Environment.NewLine, console.Lines));
    }

    [Fact]
    public async Task ListFromFile()
    {
        var manifest = Manifest.Empty
            .AddSdk(new InstalledSdk() {
                SdkVersion = new(42, 42, 42),
                RuntimeVersion = new(42, 42, 42),
                AspNetVersion = new(42, 42, 42),
                ReleaseVersion = new(42, 42, 42),
            }, new Channel.Latest());

        using var testEnv = new TestEnv(DnvmEnv.DefaultDotnetFeedUrls[0], DnvmEnv.DefaultReleasesUrl);
        await Manifest.WriteManifestUnsafe(testEnv.DnvmEnv, manifest);

        var ret = await ListCommand.Run(_logger, testEnv.DnvmEnv);
        Assert.Equal(0, ret);
        var output = $"""
DNVM_HOME: {testEnv.DnvmEnv.RealPath(UPath.Root)}

Installed SDKs:

┌───┬──────────┬─────────┬──────────┐
│   │ Version  │ Channel │ Location │
├───┼──────────┼─────────┼──────────┤
│ * │ 42.42.42 │ latest  │ dn       │
└───┴──────────┴─────────┴──────────┘

Tracked channels:

 • latest
""";

        Assert.Equal(output, string.Join(Environment.NewLine, ((TestConsole)testEnv.DnvmEnv.Console).Lines));
    }

    [Fact]
    public async Task ListTracked()
    {
        var manifest = Manifest.Empty
            .AddSdk(new SemVersion(42, 42, 42), new Channel.Latest())
            .AddSdk(new SemVersion(10, 10, 10), new Channel.Lts());

        var envVars = new Dictionary<string, string>();
        using var userHome = TestUtils.CreateTempDirectory();
        var console = new TestConsole();
        var env = new DnvmEnv(
            userHome.Path,
            new MemoryFileSystem(),
            new MemoryFileSystem(),
            UPath.Root,
            isPhysical: false,
            getUserEnvVar: s => envVars[s],
            setUserEnvVar: (name, val) => envVars[name] = val,
            console
        );
        await Manifest.WriteManifestUnsafe(env, manifest);

        var ret = await ListCommand.Run(_logger, env);
        Assert.Equal(0, ret);
        var output = """
DNVM_HOME: /

Installed SDKs:

┌───┬──────────┬─────────┬──────────┐
│   │ Version  │ Channel │ Location │
├───┼──────────┼─────────┼──────────┤
│ * │ 42.42.42 │ latest  │ dn       │
│ * │ 10.10.10 │ lts     │ dn       │
└───┴──────────┴─────────┴──────────┘

Tracked channels:

 • latest
 • lts
""";

        Assert.Equal(output, string.Join(Environment.NewLine, console.Lines));

        var consolePrefix = console.Lines.Count;
        if (UntrackCommand.RunHelper(new Channel.Latest(), manifest, env.Console) is not UntrackCommand.Result.Success({ } newManifest))
        {
            throw new InvalidOperationException();
        }
        await Manifest.WriteManifestUnsafe(env, newManifest);
        ret = await ListCommand.Run(_logger, env);
        output = """
DNVM_HOME: /

Installed SDKs:

┌───┬──────────┬─────────┬──────────┐
│   │ Version  │ Channel │ Location │
├───┼──────────┼─────────┼──────────┤
│ * │ 42.42.42 │ latest  │ dn       │
│ * │ 10.10.10 │ lts     │ dn       │
└───┴──────────┴─────────┴──────────┘

Tracked channels:

 • lts
""";

        Assert.Equal(output, string.Join(Environment.NewLine, console.Lines.Skip(consolePrefix)));
    }

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