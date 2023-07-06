
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Dnvm.Test;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm;

public sealed class SelectTests : IDisposable
{
    private readonly TestConsole _console = new();
    private readonly Logger _logger;
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;

    public SelectTests(ITestOutputHelper output)
    {
        var wrapper = new OutputWrapper(output);
        _logger = new Logger(_console);
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
            manifest = (await SelectCommand.RunWithManifest(_globalOptions.DnvmHome, previewSdkDir, manifest, _logger)).Unwrap();

            Assert.Equal(previewSdkDir, manifest.CurrentSdkDir);
            AssertSymlinkTarget(dotnetSymlink, previewSdkDir);
        });
    }

    [Fact]
    public Task BadDirName() => MockServer.WithScope(async server =>
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
        var result = await SelectCommand.RunWithManifest(_globalOptions.DnvmHome, new SdkDirName("bad"), manifest, _logger);
        Assert.Equal(SelectCommand.Result.BadDirName, result);

        Assert.Equal("""
Invalid SDK directory name: bad
Valid SDK directory names:
  dn

""", _console.Output);
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