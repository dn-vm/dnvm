
using Dnvm.Test;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm;

public sealed class SelectTests : IDisposable
{
    private readonly Logger _logger;
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;

    public SelectTests(ITestOutputHelper output)
    {
        var wrapper = new OutputWrapper(output);
        _logger = new Logger(wrapper, wrapper);
        _globalOptions = new GlobalOptions {
            DnvmHome = _dnvmHome.Path,
            UserHome = _userHome.Path,
            GetUserEnvVar = s => _envVars[s],
            SetUserEnvVar = (name, val) => _envVars[name] = val,
        };
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
    }

    [Fact]
    public async Task SelectPreview()
    {
        var mockServer = new MockServer();
        var result = await Install.Run(_globalOptions, _logger, new CommandArguments.InstallArguments {
            Channel = Channel.Latest,
            FeedUrl = mockServer.PrefixString,
        });
        Assert.Equal(Install.Result.Success, result);
        var defaultDotnet = Path.Combine(_globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name, Utilities.DotnetExeName);
        Assert.True(File.Exists(defaultDotnet));
        result = await Install.Run(_globalOptions, _logger, new CommandArguments.InstallArguments {
            Channel = Channel.Preview,
            FeedUrl = mockServer.PrefixString,
        });
        Assert.Equal(Install.Result.Success, result);
        var previewDotnet = Path.Combine(_globalOptions.DnvmHome, "preview", Utilities.DotnetExeName);
        Assert.True(File.Exists(previewDotnet));
        // Symlink should point to the default SDK
        var dotnetSymlink = Path.Combine(_globalOptions.DnvmHome, Utilities.DotnetExeName);
        var finfo = new FileInfo(dotnetSymlink);
        Assert.Equal(defaultDotnet, finfo.LinkTarget);

        // Select the preview SDK
        var manifest = ManifestUtils.ReadManifest(_globalOptions.ManifestPath);
        Assert.Equal(GlobalOptions.DefaultSdkDirName, manifest.CurrentSdkDir);

        var previewSdkDir = new SdkDirName("preview");
        manifest = await SelectCommand.SelectNewDir(_globalOptions.DnvmHome, previewSdkDir, manifest);

        Assert.Equal(previewSdkDir, manifest.CurrentSdkDir);
        finfo = new FileInfo(dotnetSymlink);
        Assert.Equal(previewDotnet, finfo.LinkTarget);
    }
}