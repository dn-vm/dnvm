
using System.Runtime.CompilerServices;
using Dnvm.Test;
using Spectre.Console.Testing;
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
        _logger = new Logger(new TestConsole());
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
        await TaskScope.With(async scope =>
        {
            await using var mockServer = new MockServer(scope);
            var result = await InstallCommand.Run(_globalOptions, _logger, new CommandArguments.InstallArguments
            {
                Channel = Channel.Latest,
                FeedUrl = mockServer.PrefixString,
            });
            Assert.Equal(InstallCommand.Result.Success, result);
            var defaultSdkDir = GlobalOptions.DefaultSdkDirName;
            var defaultDotnet = Path.Combine(_globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name, Utilities.DotnetExeName);
            Assert.True(File.Exists(defaultDotnet));
            result = await InstallCommand.Run(_globalOptions, _logger, new CommandArguments.InstallArguments
            {
                Channel = Channel.Preview,
                FeedUrl = mockServer.PrefixString,
            });
            Assert.Equal(InstallCommand.Result.Success, result);
            var previewDotnet = Path.Combine(_globalOptions.DnvmHome, "preview", Utilities.DotnetExeName);
            Assert.True(File.Exists(previewDotnet));

            // Check that the dotnet link/cmd points to the default SDK
            var dotnetSymlink = Path.Combine(_globalOptions.DnvmHome, Utilities.DotnetSymlinkName);
            AssertSymlinkTarget(dotnetSymlink, defaultSdkDir);

            // Select the preview SDK
            var manifest = ManifestUtils.ReadManifest(_globalOptions.ManifestPath);
            Assert.Equal(GlobalOptions.DefaultSdkDirName, manifest.CurrentSdkDir);

            var previewSdkDir = new SdkDirName("preview");
            manifest = await SelectCommand.SelectNewDir(_globalOptions.DnvmHome, previewSdkDir, manifest);

            Assert.Equal(previewSdkDir, manifest.CurrentSdkDir);
            AssertSymlinkTarget(dotnetSymlink, previewSdkDir);
        });
    }

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