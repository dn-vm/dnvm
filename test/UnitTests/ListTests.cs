using Semver;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using Zio.FileSystems;

namespace Dnvm.Test;

public sealed class ListTests
{
    private readonly TestConsole _console = new();
    private readonly Logger _logger;

    public ListTests()
    {
       _logger = new Logger(_console);
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

        ListCommand.PrintSdks(_logger, manifest);
        var output = """
Installed SDKs:

┌───┬────────────────┬─────────┬──────────┐
│   │ Version        │ Channel │ Location │
├───┼────────────────┼─────────┼──────────┤
│ * │ 1.0.0          │ latest  │ dn       │
│   │ 4.0.0-preview1 │ preview │ preview  │
└───┴────────────────┴─────────┴──────────┘
""";

        Assert.Equal(output, string.Join(Environment.NewLine, _console.Lines));
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

        var env = new Dictionary<string, string>();
        using var userHome = new TempDirectory();
        var home = new DnvmEnv(
            userHome.Path,
            new MemoryFileSystem(),
            isPhysical: false,
            getUserEnvVar: s => env[s],
            setUserEnvVar: (name, val) => env[name] = val
        );
        home.WriteManifest(manifest);

        var ret = await ListCommand.Run(_logger, home);
        Assert.Equal(0, ret);
        var output = """
Installed SDKs:

┌───┬──────────┬─────────┬──────────┐
│   │ Version  │ Channel │ Location │
├───┼──────────┼─────────┼──────────┤
│ * │ 42.42.42 │ latest  │ dn       │
└───┴──────────┴─────────┴──────────┘
""";

        Assert.Equal(output, string.Join(Environment.NewLine, _console.Lines));
    }

    [Fact]
    public async Task ListTracked()
    {
        var manifest = Manifest.Empty
            .AddSdk(new SemVersion(42, 42, 42), new Channel.Latest())
            .AddSdk(new SemVersion(10, 10, 10), new Channel.Lts());

        var env = new Dictionary<string, string>();
        using var userHome = new TempDirectory();
        var home = new DnvmEnv(
            userHome.Path,
            new MemoryFileSystem(),
            isPhysical: false,
            getUserEnvVar: s => env[s],
            setUserEnvVar: (name, val) => env[name] = val
        );
        home.WriteManifest(manifest);

        var ret = await ListCommand.Run(_logger, home);
        Assert.Equal(0, ret);
        var output = """
Installed SDKs:

┌───┬──────────┬─────────┬──────────┐
│   │ Version  │ Channel │ Location │
├───┼──────────┼─────────┼──────────┤
│ * │ 42.42.42 │ latest  │ dn       │
│ * │ 10.10.10 │ lts     │ dn       │
└───┴──────────┴─────────┴──────────┘

Tracked channels:

 * latest
 * lts
""";

        Assert.Equal(output, string.Join(Environment.NewLine, _console.Lines));

        var console = new TestConsole();
        var logger = new Logger(console);
        if (UntrackCommand.RunHelper(new Channel.Latest(), manifest, logger) is not UntrackCommand.Result.Success({} newManifest))
        {
            throw new InvalidOperationException();
        }
        home.WriteManifest(newManifest);
        ret = await ListCommand.Run(logger, home);
        output = """
Installed SDKs:

┌───┬──────────┬─────────┬──────────┐
│   │ Version  │ Channel │ Location │
├───┼──────────┼─────────┼──────────┤
│ * │ 42.42.42 │ latest  │ dn       │
│ * │ 10.10.10 │ lts     │ dn       │
└───┴──────────┴─────────┴──────────┘

Tracked channels:

 * lts
""";

        Assert.Equal(output, string.Join(Environment.NewLine, console.Lines));
    }
}