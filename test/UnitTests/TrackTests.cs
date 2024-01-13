using System.Collections.Immutable;
using Semver;
using Serde.Json;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;
using Zio;
using static Dnvm.TrackCommand;
using static Dnvm.Test.TestUtils;
using System.Net.Security;

namespace Dnvm.Test;

public sealed class TrackTests
{
    private readonly Logger _logger = new Logger(new TestConsole());

    [Fact]
    public Task LtsInstall() => RunWithServer(async (server, env) =>
    {
        Channel channel = new Channel.Lts();
        var options = new CommandArguments.TrackArguments()
        {
            Channel = channel,
        };
        var installCmd = new TrackCommand(env, _logger, options);
        var task = installCmd.Run();
        Result retVal = await task;
        Assert.Equal(Result.Success, retVal);
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        var dotnetFile = sdkInstallDir / (Utilities.DotnetExeName);
        Assert.True(env.Vfs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.Vfs.ReadAllText(dotnetFile));

        var manifest = await env.ReadManifest();
        var installedVersion = SemVersion.Parse(server.ReleasesIndexJson.Releases[0].LatestSdk, SemVersionStyles.Strict);
        EqArray<InstalledSdk> installedVersions = [ new InstalledSdk {
            SdkVersion = installedVersion,
            AspNetVersion = installedVersion,
            RuntimeVersion = installedVersion,
            ReleaseVersion = installedVersion,
            SdkDirName = DnvmEnv.DefaultSdkDirName
        } ];
        Assert.Equal(new Manifest
        {
            InstalledSdks = installedVersions,
            RegisteredChannels = [
                new RegisteredChannel {
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
        var args = new CommandArguments.TrackArguments()
        {
            Channel = new Channel.Lts(),
            Verbose = true,
        };
        var homeFs = env.Vfs;
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        Assert.False(homeFs.DirectoryExists(sdkInstallDir));
        Assert.True(homeFs.DirectoryExists(UPath.Root));
        Assert.Equal(Result.Success, await TrackCommand.Run(env, _logger, args));
        var dotnetFile = sdkInstallDir / (Utilities.DotnetExeName);
        Assert.True(homeFs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, homeFs.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task PreviewNotIsolated() => RunWithServer(async (server, env) =>
    {
        server.ReleasesIndexJson = server.ReleasesIndexJson with {
            Releases = server.ReleasesIndexJson.Releases.Select(r => r with { SupportPhase = "preview" }).ToImmutableArray()
        };

        var args = new CommandArguments.TrackArguments()
        {
            Channel = new Channel.Preview(),
        };
        // Preview used to be isolated, but it shouldn't be anymore
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        Assert.False(env.Vfs.DirectoryExists(sdkInstallDir));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root));
        Assert.Equal(Result.Success, await TrackCommand.Run(env, _logger, args));
        var dotnetFile = sdkInstallDir / Utilities.DotnetExeName;
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
        var args = new CommandArguments.TrackArguments()
        {
            Channel = new Channel.Sts(),
            SdkDir = dirName
        };
        // Check that the SDK is installed is isolated into the "sts" subdirectory
        var sdkInstallDir = DnvmEnv.GetSdkPath(new SdkDirName(dirName));
        Assert.False(env.Vfs.DirectoryExists(sdkInstallDir));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root));
        Assert.Equal(Result.Success, await TrackCommand.Run(env, _logger, args));
        var dotnetFile = sdkInstallDir / (Utilities.DotnetExeName);
        Assert.True(env.Vfs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.Vfs.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task ChannelOverlap() => RunWithServer(async (server, env) =>
    {
        // Default release index only contains an LTS release, so adding LTS and latest
        // should result in the same SDK being installed
        var result = await TrackCommand.Run(env, _logger, new() { Channel = new Channel.Latest() });
        Assert.Equal(TrackCommand.Result.Success , result);
        result = await TrackCommand.Run(env, _logger, new() { Channel = new Channel.Lts() });
        Assert.Equal(TrackCommand.Result.Success , result);

        var ltsRelease = server.ReleasesIndexJson.Releases.Single(r => r.ReleaseType == "lts");
        var releaseVersion = SemVersion.Parse(ltsRelease.LatestRelease, SemVersionStyles.Strict);
        var sdkVersion = SemVersion.Parse(ltsRelease.LatestSdk, SemVersionStyles.Strict);
        var manifest = await env.ReadManifest();
        Assert.Equal([ new() {
            ReleaseVersion = releaseVersion,
            SdkVersion = sdkVersion,
            RuntimeVersion = releaseVersion,
            AspNetVersion = releaseVersion,
            SdkDirName = DnvmEnv.DefaultSdkDirName
        } ], manifest.InstalledSdks);
        Assert.Equal([
            new() {
                ChannelName = new Channel.Latest(),
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = [ sdkVersion ]
            },
            new() {
                ChannelName = new Channel.Lts(),
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = [ sdkVersion ]
            }
        ], manifest.RegisteredChannels);
    });

    [Fact]
    public Task TrackMajorMinor() => RunWithServer(async (server, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new() { Channel = new Channel.Versioned(99, 99) });
        Assert.Equal(TrackCommand.Result.Success, result);

        var manifest = await env.ReadManifest();
        var version = SemVersion.Parse("99.99.99-preview", SemVersionStyles.Strict);
        Assert.Equal([ new() {
            ReleaseVersion = version,
            SdkVersion = version,
            RuntimeVersion = version,
            AspNetVersion = version,
            SdkDirName = DnvmEnv.DefaultSdkDirName
        } ], manifest.InstalledSdks);
    });

    [Fact]
    public Task TrackPreviouslyTracked() => RunWithServer(async (mockServer, env) =>
    {
        var logger = new Logger(new TestConsole());
        var channel = new Channel.Latest();
        var result = await TrackCommand.Run(env, logger, new CommandArguments.TrackArguments
        {
            Channel = channel,
        });
        Assert.Equal(TrackCommand.Result.Success, result);

        var untrackCode = await UntrackCommand.Run(env, logger, new CommandArguments.UntrackArguments
        {
            Channel = channel,
        });
        Assert.Equal(0, untrackCode);
        result = await TrackCommand.Run(env, logger, new CommandArguments.TrackArguments
        {
            Channel = channel,
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var manifest = await env.ReadManifest();
        Assert.Equal([ new RegisteredChannel {
            ChannelName = channel,
            SdkDirName = DnvmEnv.DefaultSdkDirName,
            InstalledSdkVersions = [ MockServer.DefaultLtsVersion],
            Untracked = false
        }], manifest.RegisteredChannels);
    });
}