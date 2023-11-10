
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class UninstallTests
{
    private readonly Logger _logger = new Logger(new TestConsole());

    [Fact]
    public Task LtsAndPreview() => RunWithServer(async (server, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new CommandArguments.TrackArguments
        {
            Channel = Channel.Latest,
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        result = await TrackCommand.Run(env, _logger, new CommandArguments.TrackArguments
        {
            Channel = Channel.Preview,
            SdkDir = "preview"
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var ltsVersion = SemVersion.Parse(server.ReleasesIndexJson.Releases[0].LatestSdk, SemVersionStyles.Strict);
        var previewVersion = SemVersion.Parse(server.ReleasesIndexJson.Releases[1].LatestSdk, SemVersionStyles.Strict);
        var expectedManifest = Manifest.Empty
            .AddSdk(ltsVersion, Channel.Latest, DnvmEnv.DefaultSdkDirName)
            .AddSdk(previewVersion, Channel.Preview, new SdkDirName("preview"));
        var manifest = await env.ReadManifest();
        Assert.Equal(expectedManifest, manifest);
        var unResult = await UninstallCommand.Run(env, _logger, new CommandArguments.UninstallArguments
        {
            SdkVersion = ltsVersion
        });
        Assert.Equal(0, unResult);
        manifest = await env.ReadManifest();
        var previewOnly = Manifest.Empty
            .AddSdk(previewVersion, Channel.Preview, new SdkDirName("preview"));
        previewOnly = previewOnly with {
            TrackedChannels = manifest.TrackedChannels
        };
        Assert.Equal(previewOnly, manifest);

        Assert.False(env.Vfs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.NETCore.App" / ltsVersion.ToString()));
        Assert.False(env.Vfs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.AspNetCore.App" / ltsVersion.ToString()));
        Assert.False(env.Vfs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.AspNetCore.App" / ltsVersion.ToString()));
        Assert.False(env.Vfs.DirectoryExists(UPath.Root / "dn" / "host" / "fxr" / ltsVersion.ToString()));

        Assert.True(env.Vfs.DirectoryExists(UPath.Root / "preview" / "shared" / "Microsoft.NETCore.App" / previewVersion.ToString()));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root / "preview" / "shared" / "Microsoft.AspNetCore.App" / previewVersion.ToString()));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root / "preview" / "host" / "fxr" / previewVersion.ToString()));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root / "preview" / "packs" / $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}" / previewVersion.ToString()));
        Assert.True(env.Vfs.DirectoryExists(UPath.Root / "preview" / "templates" / previewVersion.ToString()));
    });
}