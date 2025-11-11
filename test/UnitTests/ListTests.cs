using Semver;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using Zio.FileSystems;

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
}