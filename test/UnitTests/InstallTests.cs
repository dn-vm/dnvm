using System.Collections.Immutable;
using Serde.Json;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;
using static Dnvm.InstallCommand;

namespace Dnvm.Test;

public sealed class InstallTests : IDisposable
{
    private readonly Logger _logger;
    private readonly TestOptions _testOptions;

    private GlobalOptions GlobalOptions => _testOptions.GlobalOptions;

    public InstallTests(ITestOutputHelper output)
    {
        _logger = new Logger(new TestConsole());
        _testOptions = new TestOptions();
    }

    public void Dispose()
    {
        _testOptions.Dispose();
    }

    private static Task TestWithServer(Func<MockServer, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            await test(mockServer);
        });

    [Fact]
    public Task LtsInstall() => TestWithServer(async server =>
    {
        const Channel channel = Channel.Lts;
        var options = new CommandArguments.InstallArguments()
        {
            Channel = channel,
            FeedUrl = server.PrefixString,
        };
        var installCmd = new InstallCommand(GlobalOptions, _logger, options);
        var task = installCmd.Run();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var sdkInstallDir = Path.Combine(GlobalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name);
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));

        var manifest = File.ReadAllText(GlobalOptions.ManifestPath);
        var installedVersion = server.ReleasesIndexJson.Releases[0].LatestSdk;
        var installedVersions = ImmutableArray.Create(new InstalledSdk { Version = installedVersion, SdkDirName = GlobalOptions.DefaultSdkDirName });
        Assert.Equal(new Manifest
        {
            InstalledSdkVersions = installedVersions,
            TrackedChannels = ImmutableArray.Create(new[] { new TrackedChannel {
                ChannelName = channel,
                SdkDirName = GlobalOptions.DefaultSdkDirName,
                InstalledSdkVersions = ImmutableArray.Create(installedVersion)
            }})
        }, JsonSerializer.Deserialize<Manifest>(manifest));
    });

    [Fact]
    public Task SdkInstallDirMissing() => TestWithServer(async server =>
    {
        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Lts,
            FeedUrl = server.PrefixString,
            Verbose = true,
        };
        var sdkInstallDir = Path.Combine(GlobalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name);
        Assert.False(Directory.Exists(sdkInstallDir));
        Assert.True(Directory.Exists(GlobalOptions.DnvmHome));
        Assert.Equal(Result.Success, await InstallCommand.Run(GlobalOptions, _logger, args));
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task PreviewIsolated() => TestWithServer(async server =>
    {
        server.ReleasesIndexJson = server.ReleasesIndexJson with {
            Releases = server.ReleasesIndexJson.Releases.Select(r => r with { SupportPhase = "preview" }).ToImmutableArray()
        };

        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Preview,
            FeedUrl = server.PrefixString,
        };
        // Check that the preview install is isolated into a "preview" subdirectory
        var sdkInstallDir = Path.Combine(GlobalOptions.DnvmHome, Channel.Preview.ToString().ToLowerInvariant());
        Assert.False(Directory.Exists(sdkInstallDir));
        Assert.True(Directory.Exists(GlobalOptions.DnvmHome));
        Assert.Equal(Result.Success, await InstallCommand.Run(GlobalOptions, _logger, args));
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task InstallStsToSubdir() => TestWithServer(async server =>
    {
        server.ReleasesIndexJson = server.ReleasesIndexJson with {
            Releases = server.ReleasesIndexJson.Releases.Select(r => r with { ReleaseType = "sts" }).ToImmutableArray()
        };
        const string dirName = "sts";
        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Sts,
            FeedUrl = server.PrefixString,
            SdkDir = dirName
        };
        // Check that the SDK is installed is isolated into the "sts" subdirectory
        var sdkInstallDir = Path.Combine(GlobalOptions.DnvmHome, dirName);
        Assert.False(Directory.Exists(sdkInstallDir));
        Assert.True(Directory.Exists(GlobalOptions.DnvmHome));
        Assert.Equal(Result.Success, await InstallCommand.Run(GlobalOptions, _logger, args));
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    });
}