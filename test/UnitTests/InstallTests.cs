using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;
using static Dnvm.Install;

namespace Dnvm.Test;

public class InstallTests
{
    [Fact]
    public async Task Install()
    {
        using var tempDir = TestUtils.CreateTempDirectory();
        using var server = new MockServer();
        var options = new Command.InstallOptions()
        {
            Channel = Channel.Lts,
            FeedUrl = server.PrefixString,
            DnvmInstallPath = tempDir.Path,
            UpdateUserEnvironment = false,
        };
        var logger = new Logger();
        var installCmd = new Install(logger, options);
        var task = installCmd.Handle();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var dotnetFile = Path.Combine(installCmd.SdkInstallDir, "dotnet");
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));

        var manifest = File.ReadAllText(Path.Combine(tempDir.Path, ManifestUtils.FileName));
        Assert.Equal("""
{"version":2,"installedVersions":["42.42.42"],"trackedChannels":[{"channelName":"lts","installedVersions":["42.42.42"]}]}
""", manifest);
    }

    [Fact]
    public async Task InstallDirMissing()
    {
        using var tempDir = TestUtils.CreateTempDirectory();
        using var server = new MockServer();
        var installPath = Path.Combine(tempDir.Path, "subdir");
        var options = new Command.InstallOptions()
        {
            Channel = Channel.Lts,
            FeedUrl = server.PrefixString,
            DnvmInstallPath = installPath,
            UpdateUserEnvironment = false,
        };
        var logger = new Logger();
        var installCmd = new Install(logger, options);
        var task = installCmd.Handle();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var dotnetFile = Path.Combine(installCmd.SdkInstallDir, "dotnet");
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    }
}