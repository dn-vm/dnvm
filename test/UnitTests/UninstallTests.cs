
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