using System.Collections.Immutable;
using Serde.Json;
using Xunit;
using Xunit.Abstractions;
using static Dnvm.Install;

namespace Dnvm.Test;

public sealed class InstallTests : IDisposable
{
    private readonly Logger _logger;
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;

    public InstallTests(ITestOutputHelper output)
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
    public async Task LtsInstall()
    {
        using var installDir = TestUtils.CreateTempDirectory();
        using var dnvmHome = TestUtils.CreateTempDirectory();
        await using var server = new MockServer();
        const Channel channel = Channel.Lts;
        var options = new CommandArguments.InstallArguments()
        {
            Channel = channel,
            FeedUrl = server.PrefixString,
            DnvmInstallPath = installDir.Path,
            UpdateUserEnvironment = false,
        };
        var installCmd = new Install(_globalOptions, _logger, options);
        var task = installCmd.Run();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var dotnetFile = Path.Combine(_globalOptions.SdkInstallDir, "dotnet");
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));

        var manifest = File.ReadAllText(_globalOptions.ManifestPath);
        var installedVersions = ImmutableArray.Create(server.ReleasesIndexJson.Releases[0].LatestSdk);
        Assert.Equal(new Manifest
        {
            InstalledSdkVersions = installedVersions,
            TrackedChannels = ImmutableArray.Create(new[] { new TrackedChannel {
                ChannelName = channel,
                InstalledSdkVersions = installedVersions
            }})
        }, JsonSerializer.Deserialize<Manifest>(manifest));
    }

    [Fact]
    public async Task SdkInstallDirMissing()
    {
        await using var server = new MockServer();
        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Lts,
            FeedUrl = server.PrefixString,
            UpdateUserEnvironment = false,
            Verbose = true,
        };
        Assert.False(Directory.Exists(_globalOptions.SdkInstallDir));
        Assert.True(Directory.Exists(_globalOptions.DnvmHome));
        Assert.Equal(Result.Success, await Install.Run(_globalOptions, _logger, args));
        var dotnetFile = Path.Combine(_globalOptions.SdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    }
}