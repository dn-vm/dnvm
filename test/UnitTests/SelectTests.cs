
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
        var defaultSdkDir = GlobalOptions.DefaultSdkDirName;
        var defaultDotnet = Path.Combine(globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name, Utilities.DotnetExeName);
        Assert.True(File.Exists(defaultDotnet));
        result = await InstallCommand.Run(globalOptions, _logger, new CommandArguments.InstallArguments
        {
            Channel = Channel.Preview,
        });
        Assert.Equal(InstallCommand.Result.Success, result);
        var previewDotnet = Path.Combine(globalOptions.DnvmHome, "preview", Utilities.DotnetExeName);
        Assert.True(File.Exists(previewDotnet));

        // Check that the dotnet link/cmd points to the default SDK
        var dotnetSymlink = Path.Combine(globalOptions.DnvmHome, Utilities.DotnetSymlinkName);
        AssertSymlinkTarget(dotnetSymlink, defaultSdkDir);

        // Select the preview SDK
        var manifest = ManifestUtils.ReadManifest(globalOptions.ManifestPath);
        Assert.Equal(GlobalOptions.DefaultSdkDirName, manifest.CurrentSdkDir);

        var previewSdkDir = new SdkDirName("preview");
        manifest = (await SelectCommand.RunWithManifest(globalOptions.DnvmHome, previewSdkDir, manifest, _logger)).Unwrap();

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
        var result = await SelectCommand.RunWithManifest(globalOptions.DnvmHome, new SdkDirName("bad"), manifest, _logger);
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