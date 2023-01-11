using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Serde.Json;
using Xunit;
using Xunit.Abstractions;
using static Dnvm.Install;

namespace Dnvm.Test;

public class InstallTests
{
    [Fact]
    public async Task LtsInstall()
    {
        using var installDir = TestUtils.CreateTempDirectory();
        using var dnvmHome = TestUtils.CreateTempDirectory();
        using var server = new MockServer();
        const Channel channel = Channel.Lts;
        var options = new CommandArguments.InstallArguments()
        {
            Channel = channel,
            FeedUrl = server.PrefixString,
            DnvmInstallPath = installDir.Path,
            UpdateUserEnvironment = false,
        };
        var logger = new Logger();
        var installCmd = new Install(dnvmHome.Path, logger, options);
        var task = installCmd.Run();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var dotnetFile = Path.Combine(installCmd.SdkInstallDir, "dotnet");
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));

        var manifest = File.ReadAllText(Path.Combine(dnvmHome.Path, ManifestUtils.FileName));
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
    public async Task InstallDirMissing()
    {
        using var dnvmHome = TestUtils.CreateTempDirectory();
        using var installDir = TestUtils.CreateTempDirectory();
        using var server = new MockServer();
        var installPath = Path.Combine(installDir.Path, "subdir");
        var options = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Lts,
            FeedUrl = server.PrefixString,
            DnvmInstallPath = installPath,
            UpdateUserEnvironment = false,
        };
        var logger = new Logger();
        var installCmd = new Install(dnvmHome.Path, logger, options);
        var task = installCmd.Run();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var dotnetFile = Path.Combine(installCmd.SdkInstallDir, "dotnet");
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    }

    [Fact]
    public async Task DnvmHomeAndInstallCanBeDifferent()
    {
        using var dnvmHome = TestUtils.CreateTempDirectory();
        using var installDir = TestUtils.CreateTempDirectory();
        using var server = new MockServer();
        var options = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Lts,
            FeedUrl = server.PrefixString,
            DnvmInstallPath = installDir.Path,
            UpdateUserEnvironment = false,
        };
        var logger = new Logger();
        var installCmd = new Install(dnvmHome.Path, logger, options);
        Assert.Equal(Result.Success, await installCmd.Run());
        Assert.Equal(dnvmHome.Path, Path.GetDirectoryName(installCmd.SdkInstallDir));
        Assert.True(File.Exists(Path.Combine(installCmd.SdkInstallDir, "dotnet")));
        Assert.True(File.Exists(Path.Combine(dnvmHome.Path, ManifestUtils.FileName)));
        Assert.False(File.Exists(Path.Combine(installDir.Path, ManifestUtils.FileName)));
    }
}