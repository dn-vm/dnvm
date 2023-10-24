using System.Collections.Immutable;
using Serde.Json;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;
using Zio;
using static Dnvm.InstallCommand;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class InstallTests
{
    private readonly Logger _logger = new Logger(new TestConsole());

    [Fact]
    public Task LtsInstall() => RunWithServer(async (server, env) =>
    {
        const Channel channel = Channel.Lts;
        var options = new CommandArguments.InstallArguments()
        {
            Channel = channel,
        };
        var installCmd = new InstallCommand(env, _logger, options);
        var task = installCmd.Run();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        var dotnetFile = sdkInstallDir / (Utilities.DotnetExeName);
        Assert.True(env.Vfs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.Vfs.ReadAllText(dotnetFile));

        var manifest = env.ReadManifest();
        var installedVersion = server.ReleasesIndexJson.Releases[0].LatestSdk;
        EqArray<InstalledSdk> installedVersions = [ new InstalledSdk { Version = installedVersion, SdkDirName = DnvmEnv.DefaultSdkDirName } ];
        Assert.Equal(new Manifest
        {
            InstalledSdkVersions = installedVersions,
            TrackedChannels = [
                new TrackedChannel {
                        ChannelName = channel,
                        SdkDirName = DnvmEnv.DefaultSdkDirName,
                        InstalledSdkVersions = [ installedVersion ]
                    },
            ]
        }, manifest);
    });

    [Fact]
    public Task SdkInstallDirMissing() => RunWithServer(async (server, env) =>
    {
        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Lts,
            Verbose = true,
        };
        var homeFs = env.Vfs;
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        Assert.False(homeFs.DirectoryExists(sdkInstallDir));
        Assert.True(homeFs.DirectoryExists(UPath.Root));
        Assert.Equal(Result.Success, await InstallCommand.Run(env, _logger, args));
        var dotnetFile = sdkInstallDir / (Utilities.DotnetExeName);
        Assert.True(homeFs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, homeFs.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task PreviewIsolated() => RunWithServer(async (server, env) =>
    {
        server.ReleasesIndexJson = server.ReleasesIndexJson with {
            Releases = server.ReleasesIndexJson.Releases.Select(r => r with { SupportPhase = "preview" }).ToImmutableArray()
        };

        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Preview,
        };
        // Check that the preview install is isolated into a "preview" subdirectory
        var sdkInstallDir = DnvmEnv.GetSdkPath(new SdkDirName("preview"));
        Assert.False(env.Vfs.DirectoryExists(sdkInstallDir));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root));
        Assert.Equal(Result.Success, await InstallCommand.Run(env, _logger, args));
        var dotnetFile = sdkInstallDir / (Utilities.DotnetExeName);
        Assert.True(env.Vfs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.Vfs.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task InstallStsToSubdir() => RunWithServer(async (server, env) =>
    {
        server.ReleasesIndexJson = server.ReleasesIndexJson with {
            Releases = server.ReleasesIndexJson.Releases.Select(r => r with { ReleaseType = "sts" }).ToImmutableArray()
        };
        const string dirName = "sts";
        var args = new CommandArguments.InstallArguments()
        {
            Channel = Channel.Sts,
            SdkDir = dirName
        };
        // Check that the SDK is installed is isolated into the "sts" subdirectory
        var sdkInstallDir = DnvmEnv.GetSdkPath(new SdkDirName(dirName));
        Assert.False(env.Vfs.DirectoryExists(sdkInstallDir));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root));
        Assert.Equal(Result.Success, await InstallCommand.Run(env, _logger, args));
        var dotnetFile = sdkInstallDir / (Utilities.DotnetExeName);
        Assert.True(env.Vfs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.Vfs.ReadAllText(dotnetFile));
    });
}