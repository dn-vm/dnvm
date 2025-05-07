
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
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var homeFs = env.DnvmHomeFs;
        var defaultSdkDir = DnvmEnv.DefaultSdkDirName;
        var defaultDotnet = DnvmEnv.GetSdkPath(defaultSdkDir) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(defaultDotnet));
        result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Preview(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var previewDotnet = DnvmEnv.GetSdkPath(defaultSdkDir) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(previewDotnet));

        if (!OperatingSystem.IsWindows())
        {
            // Check that the dotnet link/cmd points to the default SDK
            AssertSdkDir(defaultSdkDir, env);
        }

        // Select the preview SDK
        var manifest = await env.ReadManifest();
        Assert.Equal(defaultSdkDir, manifest.CurrentSdkDir);

        manifest = (await SelectCommand.RunWithManifest(env, defaultSdkDir, manifest, _logger)).Unwrap();

        Assert.Equal(defaultSdkDir, manifest.CurrentSdkDir);
        AssertSdkDir(defaultSdkDir, env);
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

    private static void AssertSdkDir(
        SdkDirName dirName,
        DnvmEnv env)
    {
        if (OperatingSystem.IsWindows())
        {
            // On Windows read the PATH environment variable and check that it contains the target SDK directory
            var path = env.GetUserEnvVar("PATH") ?? string.Empty;
            Assert.Contains(env.RealPath(DnvmEnv.GetSdkPath(dirName)), path.Split(";"));
        }
        else
        {
            // On unix read the target symlink and check that it points to the correct directory
            var dotnetSymlinkPath = env.RealPath(DnvmEnv.SymlinkPath);
            var finfo = new FileInfo(dotnetSymlinkPath);
            Assert.EndsWith(Path.Combine(dirName.Name, Utilities.DotnetExeName), finfo.LinkTarget);
        }
    }
}