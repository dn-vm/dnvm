
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class SelectTests
{
    private readonly TestConsole _console = new();
    private readonly Logger _logger;

    public SelectTests(ITestOutputHelper output)
    {
        _logger = new Logger(_console);
    }

    [Fact]
    public Task SelectPreview() => TestUtils.RunWithServer(async (mockServer, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new CommandArguments.TrackArguments
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var homeFs = env.HomeFs;
        var defaultSdkDir = DnvmEnv.DefaultSdkDirName;
        var defaultDotnet = DnvmEnv.GetSdkPath(defaultSdkDir) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(defaultDotnet));
        result = await TrackCommand.Run(env, _logger, new CommandArguments.TrackArguments
        {
            Channel = new Channel.Preview(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var previewDotnet = DnvmEnv.GetSdkPath(defaultSdkDir) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(previewDotnet));

        // Check that the dotnet link/cmd points to the default SDK
        var dotnetSymlink = env.RealPath(DnvmEnv.SymlinkPath);
        AssertSymlinkTarget(dotnetSymlink, defaultSdkDir);

        // Select the preview SDK
        var manifest = await env.ReadManifest();
        Assert.Equal(defaultSdkDir, manifest.CurrentSdkDir);

        manifest = (await SelectCommand.RunWithManifest(env, defaultSdkDir, manifest, _logger)).Unwrap();

        Assert.Equal(defaultSdkDir, manifest.CurrentSdkDir);
        AssertSymlinkTarget(dotnetSymlink, defaultSdkDir);
    });

    [Fact]
    public Task BadDirName() => TestUtils.RunWithServer(async (server, env) =>
    {
        var dn = new SdkDirName("dn");
        var manifest = new Manifest()
        {
            CurrentSdkDir = dn,
            RegisteredChannels =

            [
                new RegisteredChannel
                    {
                        ChannelName = new Channel.Latest(),
                        SdkDirName = dn
                    },
            ]
        };
        var result = await SelectCommand.RunWithManifest(env, new SdkDirName("bad"), manifest, _logger);
        Assert.Equal(SelectCommand.Result.BadDirName, result);

        Assert.Equal("""
Invalid SDK directory name: bad
Valid SDK directory names:
  dn

""".Replace(Environment.NewLine, "\n"), _console.Output);
    });

    private static void AssertSymlinkTarget(string dotnetSymlink, SdkDirName dirName)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains($"{dirName.Name}\\{Utilities.DotnetExeName}", File.ReadAllText(dotnetSymlink));
        }
        else
        {
            var finfo = new FileInfo(dotnetSymlink);
            Assert.EndsWith(Path.Combine(dirName.Name, Utilities.DotnetExeName), finfo.LinkTarget);
        }
    }
}