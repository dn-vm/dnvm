
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

        // Under new prune logic, no SDKs should be removed because each channel
        // only has one version (Latest has 42.42.42, Preview in dn has 42.42.43,
        // Preview in preview has 42.42.42). Different channels don't prune each other.
        List<(SemVersion, SdkDirName)> expected = [];
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
    public void PruneOnlyRemovesSDKsFromTrackedChannels()
    {
        // Start with empty manifest and add SDKs with proper channel tracking
        var manifest = Manifest.Empty
            // Add SDKs installed through tracked channels (these get tracked in the channel)
            .AddSdk(SemVersion.Parse("8.0.100", SemVersionStyles.Strict), new Channel.VersionedMajorMinor(8, 0))
            .AddSdk(SemVersion.Parse("8.0.102", SemVersionStyles.Strict), new Channel.VersionedMajorMinor(8, 0))
            .AddSdk(SemVersion.Parse("9.0.101", SemVersionStyles.Strict), new Channel.Latest());

        // Add manually installed SDKs (no channel specified = not tracked by any channel)
        manifest = manifest
            .AddSdk(SemVersion.Parse("8.0.101", SemVersionStyles.Strict)) // Manual install - no channel
            .AddSdk(SemVersion.Parse("9.0.100", SemVersionStyles.Strict)); // Manual install - no channel

        // Add an untracked channel with its SDK
        manifest = manifest with
        {
            RegisteredChannels = manifest.RegisteredChannels.Add(new RegisteredChannel
            {
                ChannelName = new Channel.Preview(),
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = [SemVersion.Parse("8.0.99", SemVersionStyles.Strict)], // RC version tracked by this channel
                Untracked = true
            })
        };
        // Add the actual SDK for the untracked channel
        manifest = manifest.AddSdk(SemVersion.Parse("8.0.99", SemVersionStyles.Strict));

        var outOfDate = PruneCommand.GetOutOfDateSdks(manifest);

        // Should only prune older versions from tracked channels
        // 8.0.100 should be removed (older than 8.0.102 in same tracked channel "8.0")
        // 8.0.101 should NOT be removed (manually installed, not tracked by any channel)
        // 8.0.99 should NOT be removed (from untracked channel)
        // 9.0.100 should NOT be removed (manually installed, not tracked by any channel)
        // 9.0.101 should remain (only version in its tracked channel)
        var expectedToRemove = new List<(SemVersion, SdkDirName)> { (SemVersion.Parse("8.0.100", SemVersionStyles.Strict), DnvmEnv.DefaultSdkDirName) };

        Assert.Equal(expectedToRemove, outOfDate);
    }

    [Fact]
    public void PruneDoesNotAffectManuallyInstalledSDKs()
    {
        // Test that manually installed SDKs (and those from global.json restore)
        // are never pruned, even if newer versions exist
        var manifest = Manifest.Empty
            // Manually installed SDKs (no channel specified)
            .AddSdk(SemVersion.Parse("8.0.100", SemVersionStyles.Strict)) // Older manual install
            .AddSdk(SemVersion.Parse("8.0.105", SemVersionStyles.Strict)) // Newer manual install
            // Channel-tracked SDK that's newer than manual ones
            .AddSdk(SemVersion.Parse("8.0.102", SemVersionStyles.Strict), new Channel.Latest())
            .AddSdk(SemVersion.Parse("8.0.103", SemVersionStyles.Strict), new Channel.Latest());

        var outOfDate = PruneCommand.GetOutOfDateSdks(manifest);

        // Only the older version from the tracked channel should be pruned
        // Manual installs should never be pruned, even though 8.0.100 is older than 8.0.105
        var expected = new List<(SemVersion, SdkDirName)> {
            (SemVersion.Parse("8.0.102", SemVersionStyles.Strict), DnvmEnv.DefaultSdkDirName)
        };

        Assert.Equal(expected, outOfDate);
    }

    [Fact]
    public void PruneConsidersVersionsWithinSameChannel()
    {
        // This test verifies that prune considers versions only within the same channel
        var manifest = Manifest.Empty
            // Channel A: 8.0 Latest - has two versions, should prune older one
            .AddSdk(SemVersion.Parse("8.0.100", SemVersionStyles.Strict), new Channel.VersionedMajorMinor(8, 0))
            .AddSdk(SemVersion.Parse("8.0.102", SemVersionStyles.Strict), new Channel.VersionedMajorMinor(8, 0))
            // Channel B: 8.0 Preview - has one version, should not prune
            .AddSdk(SemVersion.Parse("8.0.101", SemVersionStyles.Strict), new Channel.Preview())
            // Channel C: 9.0 Latest - has two versions in different major.minor, should not prune
            .AddSdk(SemVersion.Parse("9.0.100", SemVersionStyles.Strict), new Channel.VersionedMajorMinor(9, 0))
            .AddSdk(SemVersion.Parse("9.1.100", SemVersionStyles.Strict), new Channel.VersionedMajorMinor(9, 0));

        var outOfDate = PruneCommand.GetOutOfDateSdks(manifest);

        // Only 8.0.100 should be pruned (older version in 8.0 Latest channel)
        // 8.0.101 should NOT be pruned (different channel - Preview)
        // 9.0.100 and 9.1.100 should NOT be pruned (different major.minor versions)
        var expected = new List<(SemVersion, SdkDirName)> {
            (SemVersion.Parse("8.0.100", SemVersionStyles.Strict), DnvmEnv.DefaultSdkDirName)
        };

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