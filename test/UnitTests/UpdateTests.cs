
using System.Collections.Immutable;
using System.Runtime.InteropServices.Marshalling;
using Semver;
using Serde.Json;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class UpdateTests
{
    private readonly Logger _logger;
    private readonly CommandArguments.UpdateArguments updateArguments = new() {
        Verbose = true,
        Yes = true,
    };

    public UpdateTests(ITestOutputHelper output)
    {
        _logger = new Logger(new TestConsole());
    }

    private static Task TestWithServer(Func<MockServer, DnvmEnv, CancellationToken, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl);
            await test(mockServer, testOptions.DnvmEnv, taskScope.CancellationToken);
        });

    [Fact]
    public Task UpdateChecksForSelfUpdate() => TestWithServer(async (mockServer, env, cancellationToken) =>
    {
        var sdkDir = DnvmEnv.DefaultSdkDirName;
        var manifest = new Manifest
        {
            InstalledSdkVersions = ImmutableArray.Create(new InstalledSdk { Version = "42.42.142", SdkDirName = sdkDir }),
            TrackedChannels = ImmutableArray.Create(new TrackedChannel
            {
                ChannelName = Channel.Latest,
                SdkDirName = sdkDir,
                InstalledSdkVersions = ImmutableArray.Create("42.42.142")
            })
        };
        var releasesIndex = mockServer.ReleasesIndexJson;
        var console = new TestConsole();
        var logger = new Logger(console);
        _ = await UpdateCommand.UpdateSdks(
            env,
            logger,
            releasesIndex,
            manifest,
            yes: false,
            env.DotnetFeedUrl,
            env.DnvmReleasesUrl!,
            cancellationToken);
        Assert.Contains("dnvm is out of date", console.Output);
    });

    [Fact]
    public Task FindsNewerLatestToLtsVersion() => TestUtils.RunWithServer(mockServer =>
    {
        // Construct a manifest with an installed version "41.0.0" in the Latest channel
        // and confirm that 42.42 is processed as newer
        var installedVersion = "41.0.0";
        var manifest = new Manifest {
            InstalledSdkVersions = ImmutableArray.Create(new InstalledSdk { Version = installedVersion, SdkDirName = DnvmEnv.DefaultSdkDirName }),
            TrackedChannels = ImmutableArray.Create(new TrackedChannel {
                ChannelName = Channel.Latest,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = ImmutableArray.Create(installedVersion)
            })
        };
        var releasesIndex = mockServer.ReleasesIndexJson;
        var results = UpdateCommand.FindPotentialUpdates(manifest, releasesIndex);
        var (channel, newestInstalled, newestAvailable) = results[0];
        Assert.Equal(Channel.Latest, channel);
        Assert.Equal(new SemVersion(41, 0, 0), newestInstalled);
        Assert.Equal("42.42.42", newestAvailable!.LatestRelease);
        Assert.Single(results);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task InstallAndUpdate() => await TestWithServer(async (mockServer, env, cancellationToken) =>
    {
        const Channel channel = Channel.Latest;
        mockServer.ReleasesIndexJson = new() {
            Releases = ImmutableArray.Create(new DotnetReleasesIndex.Release[] {
                new() {
                    LatestRelease = "41.0.0",
                    LatestSdk = "41.0.100",
                    MajorMinorVersion = "41.0",
                    ReleaseType = "lts",
                    SupportPhase = "active"
                }
            })
        };
        var result = await InstallCommand.Run(env, _logger, new() {
            Channel = channel,
            Verbose = true
        });
        Assert.Equal(InstallCommand.Result.Success, result);
        // Update with a newer version
        mockServer.ReleasesIndexJson = new() {
            Releases = ImmutableArray.Create(new DotnetReleasesIndex.Release[] {
                new() {
                    LatestRelease = "41.0.1",
                    LatestSdk = "41.0.101",
                    MajorMinorVersion = "41.0",
                    ReleaseType = "lts",
                    SupportPhase = "active"
                }
            })
        };
        var updateResult = await UpdateCommand.Run(env, _logger, updateArguments);
        var sdkVersions = ImmutableArray.Create(new[] { "41.0.100", "41.0.101" });
        Assert.Equal(UpdateCommand.Result.Success, updateResult);
        var expectedManifest = new Manifest {
            InstalledSdkVersions = sdkVersions.Select(v => new InstalledSdk { Version = v, SdkDirName = DnvmEnv.DefaultSdkDirName }).ToImmutableArray(),
            TrackedChannels = ImmutableArray.Create(new[] { new TrackedChannel() {
                ChannelName = channel,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = sdkVersions
            }})
        };
        var actualManifest = env.ReadManifest();
        Assert.Equal(expectedManifest, actualManifest);
    });

    [Fact]
    public void SemVerIsParsable()
    {
        Assert.NotNull(Program.SemVer); // Should throw if version is not parsable.
    }

    [Fact]
    public void LatestLtsStsDoNotAcceptPreview()
    {
        var ltsRelease = new DotnetReleasesIndex.Release
        {
            LatestRelease = "42.42.42",
            LatestSdk = "42.42.42",
            MajorMinorVersion = "42.42",
            ReleaseType = "lts",
            SupportPhase = "active"
        };
        var stsRelease = new DotnetReleasesIndex.Release {
            LatestRelease = "50.50.50",
            LatestSdk = "50.50.50",
            MajorMinorVersion = "50.50",
            ReleaseType = "sts",
            SupportPhase = "active"
        };
        var ltsPreview = new DotnetReleasesIndex.Release {
            LatestRelease = "100.100.100-preview.1",
            LatestSdk = "100.100.100-preview.1",
            MajorMinorVersion = "100.100",
            ReleaseType = "lts",
            SupportPhase = "preview"
        };
        var stsPreview = new DotnetReleasesIndex.Release {
            LatestRelease = "99.99.99-preview.1",
            LatestSdk = "99.99.99-preview.1",
            MajorMinorVersion = "99.99",
            ReleaseType = "sts",
            SupportPhase = "preview"
        };
        var releasesIndex = new DotnetReleasesIndex {
            Releases = ImmutableArray.Create(new[] { ltsRelease, stsRelease, ltsPreview, stsPreview })
        };

        var actual = releasesIndex.GetLatestReleaseForChannel(Channel.Latest);
        // STS is newest
        Assert.Equal(stsRelease, actual);

        actual = releasesIndex.GetLatestReleaseForChannel(Channel.Lts);
        Assert.Equal(ltsRelease, actual);

        actual = releasesIndex.GetLatestReleaseForChannel(Channel.Sts);
        Assert.Equal(stsRelease, actual);

        actual = releasesIndex.GetLatestReleaseForChannel(Channel.Preview);
        // LTS preview is newest
        Assert.Equal(ltsPreview, actual);
    }
}