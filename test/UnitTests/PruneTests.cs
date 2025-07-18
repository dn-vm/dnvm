
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class PruneTests
{
    private readonly Logger _logger = new Logger(new StringWriter());

    [Fact]
    public void OutOfDateInOneDir()
    {
        var manifest = Manifest.Empty
            .AddSdk(new(42, 42, 42), new Channel.Latest(), new("dn"))
            .AddSdk(new(42, 42, 43), new Channel.Preview(), new("dn"))
            .AddSdk(new(42, 42, 42), new Channel.Preview(), new("preview"));

        var outOfDate = PruneCommand.GetOutOfDateSdks(manifest);

        List<(SemVersion, SdkDirName)> expected = [ (new(42, 42, 42), new("dn")) ];
        Assert.Equal(expected, outOfDate);
    }

    [Fact]
    public void OutOfDatePreview()
    {
        var manifest = Manifest.Empty
            .AddSdk(SemVersion.Parse("8.0.0-preview.1", SemVersionStyles.Strict), new Channel.Preview(), new("dn"))
            .AddSdk(SemVersion.Parse("8.0.0-rc.2", SemVersionStyles.Strict), new Channel.Preview(), new("dn"));

        var outOfDate = PruneCommand.GetOutOfDateSdks(manifest);

        var item1 = manifest.InstalledSdks[0];
        List<(SemVersion, SdkDirName)> expected = [ (item1.SdkVersion, item1.SdkDirName) ];
        Assert.Equal(expected, outOfDate);
    }

    [Fact]
    public Task MissingDirectoriesHandled() => RunWithServer(async (server, env) =>
    {
        // Install two SDKs to create a scenario where one can be pruned
        var baseVersion = new SemVersion(41, 0, 0);
        var upgradeVersion = new SemVersion(41, 0, 1);
        Channel channel = new Channel.Latest();
        server.ClearAndSetLts(baseVersion);
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options() {
            Channel = channel,
            Verbose = true
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        // Update with a newer version
        server.ClearAndSetLts(upgradeVersion);
        var updateResult = await UpdateCommand.Run(env, _logger, new UpdateCommand.Options()
        {
            Yes = true,
        });
        EqArray<SemVersion> sdkVersions = [ baseVersion, upgradeVersion ];
        Assert.Equal(UpdateCommand.Result.Success, updateResult);

        // Manually remove some directories for the LTS version to simulate missing directories
        var sdkDir = UPath.Root / "dn" / "sdk" / baseVersion.ToString();
        var runtimeDir = UPath.Root / "dn" / "shared" / "Microsoft.NETCore.App" / baseVersion.ToString();
        env.DnvmHomeFs.DeleteDirectory(sdkDir, isRecursive: true);
        env.DnvmHomeFs.DeleteDirectory(runtimeDir, isRecursive: true);

        var console = (TestConsole)env.Console;
        var trimOutput = console.Output;

        // Prune should succeed despite missing directories
        var pruneResult = await PruneCommand.Run(env, _logger, new PruneCommand.Options());
        var actualOutput = console.Output[trimOutput.Length..];

        Assert.Equal(0, pruneResult);
        Assert.Contains("not found, skipping", actualOutput);

        // Verify manifest is updated correctly - older version should be removed
        var finalManifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Single(finalManifest.InstalledSdks);
        Assert.Equal(upgradeVersion, finalManifest.InstalledSdks[0].SdkVersion);
    });
}