
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Dnvm.Test;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm;

public sealed class SelectTests
{
    private readonly TestConsole _console = new();
    private readonly Logger _logger;

    public SelectTests(ITestOutputHelper output)
    {
        _logger = new Logger(_console);
    }

    [Fact]
    public Task SelectPreview() => TestUtils.RunWithServer(async (mockServer, globalOptions) =>
    {
        var result = await InstallCommand.Run(globalOptions, _logger, new CommandArguments.InstallArguments
        {
            Channel = Channel.Latest,
        });
        Assert.Equal(InstallCommand.Result.Success, result);
        var env = globalOptions.DnvmEnv;
        var homeFs = env.Vfs;
        var defaultSdkDir = GlobalOptions.DefaultSdkDirName;
        var defaultDotnet = DnvmEnv.GetSdkPath(defaultSdkDir) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(defaultDotnet));
        result = await InstallCommand.Run(globalOptions, _logger, new CommandArguments.InstallArguments
        {
            Channel = Channel.Preview,
        });
        Assert.Equal(InstallCommand.Result.Success, result);
        var previewDotnet = DnvmEnv.GetSdkPath(new SdkDirName("preview")) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(previewDotnet));

        // Check that the dotnet link/cmd points to the default SDK
        var dotnetSymlink = env.RealPath(DnvmEnv.SymlinkPath);
        AssertSymlinkTarget(dotnetSymlink, defaultSdkDir);

        // Select the preview SDK
        var manifest = globalOptions.DnvmEnv.ReadManifest();
        Assert.Equal(GlobalOptions.DefaultSdkDirName, manifest.CurrentSdkDir);

        var previewSdkDir = new SdkDirName("preview");
        manifest = (await SelectCommand.RunWithManifest(env, previewSdkDir, manifest, _logger)).Unwrap();

        Assert.Equal(previewSdkDir, manifest.CurrentSdkDir);
        AssertSymlinkTarget(dotnetSymlink, previewSdkDir);
    });

    [Fact]
    public Task BadDirName() => TestUtils.RunWithServer(async (server, globalOptions) =>
    {
        var dn = new SdkDirName("dn");
        var manifest = new Manifest()
        {
            CurrentSdkDir = dn,
            TrackedChannels = ImmutableArray.Create<TrackedChannel>(new TrackedChannel
            {
                ChannelName = Channel.Latest,
                SdkDirName = dn
            })
        };
        var result = await SelectCommand.RunWithManifest(globalOptions.DnvmEnv, new SdkDirName("bad"), manifest, _logger);
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