using System.Collections.Immutable;
using Serde.Json;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;
using static Dnvm.InstallCommand;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class InstallTests
{
    private readonly Logger _logger;

    public InstallTests(ITestOutputHelper output)
    {
        _logger = new Logger(new TestConsole());
    }

    [Fact]
    public Task LtsInstall() => RunWithServer(async (server, globalOptions) =>
    {
        const Channel channel = Channel.Lts;
        var options = new CommandArguments.InstallArguments()
        {
            Channel = channel,
            FeedUrl = server.PrefixString,
        };
        var installCmd = new InstallCommand(globalOptions, _logger, options);
        var task = installCmd.Run();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var sdkInstallDir = Path.Combine(globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name);
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));

        var manifest = File.ReadAllText(globalOptions.ManifestPath);
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
    public Task SdkInstallDirMissing() => RunWithServer(async (server, globalOptions) =>
    {
        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Lts,
            FeedUrl = server.PrefixString,
            Verbose = true,
        };
        var sdkInstallDir = Path.Combine(globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name);
        Assert.False(Directory.Exists(sdkInstallDir));
        Assert.True(Directory.Exists(globalOptions.DnvmHome));
        Assert.Equal(Result.Success, await InstallCommand.Run(globalOptions, _logger, args));
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task PreviewIsolated() => RunWithServer(async (server, globalOptions) =>
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
        var sdkInstallDir = Path.Combine(globalOptions.DnvmHome, Channel.Preview.ToString().ToLowerInvariant());
        Assert.False(Directory.Exists(sdkInstallDir));
        Assert.True(Directory.Exists(globalOptions.DnvmHome));
        Assert.Equal(Result.Success, await InstallCommand.Run(globalOptions, _logger, args));
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task InstallStsToSubdir() => RunWithServer(async (server, globalOptions) =>
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
        var sdkInstallDir = Path.Combine(globalOptions.DnvmHome, dirName);
        Assert.False(Directory.Exists(sdkInstallDir));
        Assert.True(Directory.Exists(globalOptions.DnvmHome));
        Assert.Equal(Result.Success, await InstallCommand.Run(globalOptions, _logger, args));
        var dotnetFile = Path.Combine(sdkInstallDir, "dotnet" + Utilities.ExeSuffix);
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    });
}