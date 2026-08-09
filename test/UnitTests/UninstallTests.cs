
using System.Net.Security;
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class UninstallTests
{
    private readonly Logger _logger = new Logger(new StringWriter());

    [Fact]
    public Task LtsAndPreview() => RunWithServer(async (server, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Preview(),
            SdkDir = new("preview")
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var ltsVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[0].LatestSdk, SemVersionStyles.Strict);
        var previewVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[1].LatestSdk, SemVersionStyles.Strict);
        var expectedManifest = Manifest.Empty
            .AddSdk(ltsVersion, new Channel.Latest(), DnvmEnv.DefaultSdkDirName)
            .AddSdk(previewVersion, new Channel.Preview(), new SdkDirName("preview"));
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal(expectedManifest, manifest);
        var unResult = await UninstallCommand.Run(env, _logger, ltsVersion);
        Assert.Equal(0, unResult);
        manifest = await Manifest.ReadManifestUnsafe(env);
        var previewOnly = Manifest.Empty
            .AddSdk(previewVersion, new Channel.Preview(), new SdkDirName("preview"));
        previewOnly = previewOnly with {
            RegisteredChannels = manifest.RegisteredChannels
        };
        Assert.Equal(previewOnly, manifest);

        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.NETCore.App" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.AspNetCore.App" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.AspNetCore.App" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "host" / "fxr" / ltsVersion.ToString()));

        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "shared" / "Microsoft.NETCore.App" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "shared" / "Microsoft.AspNetCore.App" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "host" / "fxr" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "packs" / $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "templates" / previewVersion.ToString()));
    });

    [Fact]
    public Task DirectorySpecificUninstallKeepsOtherCopy() => RunWithServer(async (server, env) =>
    {
        var version = new SemVersion(8, 0, 100);
        var defaultDir = DnvmEnv.DefaultSdkDirName;
        var alternateDir = new SdkDirName("alternate");
        var manifest = Manifest.Empty
            .AddSdk(version, sdkDirParam: defaultDir)
            .AddSdk(version, sdkDirParam: alternateDir);
        manifest = manifest with
        {
            RegisteredChannels =
            [
                new RegisteredChannel
                {
                    ChannelName = new Channel.Latest(),
                    SdkDirName = defaultDir,
                    InstalledSdkVersions = [version],
                },
                new RegisteredChannel
                {
                    ChannelName = new Channel.VersionedMajorMinor(8, 0),
                    SdkDirName = alternateDir,
                    InstalledSdkVersions = [version],
                },
            ]
        };
        await Manifest.WriteManifestUnsafe(env, manifest);

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, version, alternateDir));

        var finalManifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Contains(finalManifest.InstalledSdks,
            sdk => sdk.SdkVersion == version && sdk.SdkDirName == defaultDir);
        Assert.DoesNotContain(finalManifest.InstalledSdks,
            sdk => sdk.SdkVersion == version && sdk.SdkDirName == alternateDir);
        Assert.Contains(finalManifest.RegisteredChannels,
            channel => channel.SdkDirName == defaultDir
                && channel.InstalledSdkVersions.Contains(version));
        Assert.Contains(finalManifest.RegisteredChannels,
            channel => channel.SdkDirName == alternateDir
                && !channel.InstalledSdkVersions.Contains(version));
    });

    [Fact]
    public Task UninstallWithoutDirectoryRemovesAllCopies() => RunWithServer(async (server, env) =>
    {
        var version = new SemVersion(8, 0, 100);
        var defaultDir = DnvmEnv.DefaultSdkDirName;
        var alternateDir = new SdkDirName("alternate");
        var manifest = Manifest.Empty
            .AddSdk(version, sdkDirParam: defaultDir)
            .AddSdk(version, sdkDirParam: alternateDir);
        manifest = manifest with
        {
            RegisteredChannels =
            [
                new RegisteredChannel
                {
                    ChannelName = new Channel.Latest(),
                    SdkDirName = defaultDir,
                    InstalledSdkVersions = [version],
                },
                new RegisteredChannel
                {
                    ChannelName = new Channel.VersionedMajorMinor(8, 0),
                    SdkDirName = alternateDir,
                    InstalledSdkVersions = [version],
                },
            ]
        };
        await Manifest.WriteManifestUnsafe(env, manifest);

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, version));

        var finalManifest = await Manifest.ReadManifestUnsafe(env);
        Assert.DoesNotContain(finalManifest.InstalledSdks, sdk => sdk.SdkVersion == version);
        Assert.All(finalManifest.RegisteredChannels,
            channel => Assert.DoesNotContain(version, channel.InstalledSdkVersions));
    });

    [Fact]
    public Task UninstallMessage() => RunWithServer(async (server, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        result = await TrackCommand.Run(env, _logger, new DnvmSubCommand.TrackArgs
        {
            Channel = new Channel.Preview(),
            SdkDir = "preview"
        });
        Assert.Equal(TrackCommand.Result.Success, result);

        var ltsVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[0].LatestSdk, SemVersionStyles.Strict);
        var previewVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[1].LatestSdk, SemVersionStyles.Strict);

        var console = (TestConsole)env.Console;
        var trimOutput = console.Output;
        var unResult = await UninstallCommand.Run(env, _logger, previewVersion);
        var actualOutput = console.Output[trimOutput.Length..];
        Assert.Equal(0, unResult);
        Assert.DoesNotContain("SdkDirName", actualOutput);
        Assert.DoesNotContain(ltsVersion.ToString(), actualOutput);
    });

    [Fact]
    public Task MissingDirectoriesHandled() => RunWithServer(async (server, env) =>
    {
        // Install an SDK first
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);

        var ltsVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[0].LatestSdk, SemVersionStyles.Strict);
        
        // Manually remove some directories to simulate missing directories
        var sdkDir = UPath.Root / "dn" / "sdk" / ltsVersion.ToString();
        var runtimeDir = UPath.Root / "dn" / "shared" / "Microsoft.NETCore.App" / ltsVersion.ToString();
        env.DnvmHomeFs.DeleteDirectory(sdkDir, isRecursive: true);
        env.DnvmHomeFs.DeleteDirectory(runtimeDir, isRecursive: true);

        var console = (TestConsole)env.Console;
        var trimOutput = console.Output;
        
        // Uninstall should succeed despite missing directories
        var unResult = await UninstallCommand.Run(env, _logger, ltsVersion);
        var actualOutput = console.Output[trimOutput.Length..];
        
        Assert.Equal(0, unResult);
        Assert.Contains("not found, skipping", actualOutput);
        
        // Verify manifest is still updated correctly
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Empty(manifest.InstalledSdks);
    });
}