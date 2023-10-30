
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
            InstalledSdkVersions = [ new InstalledSdk {
                SdkVersion = new(42, 42, 142),
                AspNetVersion = new(42, 42, 142),
                RuntimeVersion = new(42, 42, 142),
                ReleaseVersion = new(42, 42, 142),
                Channel = Channel.Latest,
                SdkDirName = sdkDir
            } ],
            TrackedChannels =
            [
                new TrackedChannel
                    {
                        ChannelName = Channel.Latest,
                        SdkDirName = sdkDir,
                        InstalledSdkVersions = [ new(42, 42, 142) ]
                    },
            ]
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
        var installedVersion = new SemVersion(41, 0, 0);
        var manifest = new Manifest {
            InstalledSdkVersions = [ new InstalledSdk {
                SdkVersion = installedVersion,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                AspNetVersion = installedVersion,
                RuntimeVersion = installedVersion,
                ReleaseVersion = installedVersion,
                Channel = Channel.Latest
            }] ,
            TrackedChannels = [ new TrackedChannel {
                ChannelName = Channel.Latest,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = [ installedVersion ]
            } ]
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
        var baseVersion = new SemVersion(41, 0, 0);
        var upgradeVersion = new SemVersion(41, 0, 1);
        const Channel channel = Channel.Latest;
        Setup(mockServer, baseVersion);
        var result = await InstallCommand.Run(env, _logger, new() {
            Channel = channel,
            Verbose = true
        });
        Assert.Equal(InstallCommand.Result.Success, result);
        // Update with a newer version
        Setup(mockServer, upgradeVersion);
        var updateResult = await UpdateCommand.Run(env, _logger, updateArguments);
        EqArray<SemVersion> sdkVersions = [ baseVersion, upgradeVersion ];
        Assert.Equal(UpdateCommand.Result.Success, updateResult);
        var expectedManifest = new Manifest {
            InstalledSdkVersions = sdkVersions.Select(v => new InstalledSdk
            {
                SdkVersion = v,
                AspNetVersion = v,
                RuntimeVersion = v,
                ReleaseVersion = v,
                Channel = channel,
                SdkDirName = DnvmEnv.DefaultSdkDirName }).ToEq(),
            TrackedChannels = [ new TrackedChannel() {
                ChannelName = channel,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = sdkVersions
            } ]
        };
        var actualManifest = await env.ReadManifest();
        Assert.Equal(expectedManifest, actualManifest);

        static void Setup(MockServer mockServer, SemVersion version)
        {
            mockServer.ReleasesIndexJson = new()
            {
                Releases = [ new() {
                        LatestRelease = version.ToString(),
                        LatestSdk = version.ToString(),
                        MajorMinorVersion = version.ToMajorMinor(),
                        ReleaseType = "lts",
                        SupportPhase = "active",
                        ChannelReleaseIndexUrl = mockServer.GetChannelIndexUrl(version.ToMajorMinor())
                    }
                ]
            };
            mockServer.ChannelIndexMap.Clear();
            mockServer.ChannelIndexMap.Add(version.ToMajorMinor(), new()
            {
                Releases = [ChannelReleaseIndex.CreateRelease(version)]
            });
        }
    });

    [Fact]
    public void SemVerIsParsable()
    {
        Assert.NotNull(Program.SemVer); // Should throw if version is not parsable.
    }

    [Fact]
    public void LatestLtsStsDoNotAcceptPreview()
    {
        var ltsRelease = new DotnetReleasesIndex.ChannelIndex
        {
            LatestRelease = "42.42.42",
            LatestSdk = "42.42.42",
            MajorMinorVersion = "42.42",
            ReleaseType = "lts",
            SupportPhase = "active",
            ChannelReleaseIndexUrl = null!
        };
        var stsRelease = new DotnetReleasesIndex.ChannelIndex {
            LatestRelease = "50.50.50",
            LatestSdk = "50.50.50",
            MajorMinorVersion = "50.50",
            ReleaseType = "sts",
            SupportPhase = "active",
            ChannelReleaseIndexUrl = null!
        };
        var ltsPreview = new DotnetReleasesIndex.ChannelIndex {
            LatestRelease = "100.100.100-preview.1",
            LatestSdk = "100.100.100-preview.1",
            MajorMinorVersion = "100.100",
            ReleaseType = "lts",
            SupportPhase = "preview",
            ChannelReleaseIndexUrl = null!
        };
        var stsPreview = new DotnetReleasesIndex.ChannelIndex {
            LatestRelease = "99.99.99-preview.1",
            LatestSdk = "99.99.99-preview.1",
            MajorMinorVersion = "99.99",
            ReleaseType = "sts",
            SupportPhase = "preview",
            ChannelReleaseIndexUrl = null!
        };
        var releasesIndex = new DotnetReleasesIndex {
            Releases = [ltsRelease, stsRelease, ltsPreview, stsPreview]
        };

        var actual = releasesIndex.GetChannelIndex(Channel.Latest);
        // STS is newest
        Assert.Equal(stsRelease, actual);

        actual = releasesIndex.GetChannelIndex(Channel.Lts);
        Assert.Equal(ltsRelease, actual);

        actual = releasesIndex.GetChannelIndex(Channel.Sts);
        Assert.Equal(stsRelease, actual);

        actual = releasesIndex.GetChannelIndex(Channel.Preview);
        // LTS preview is newest
        Assert.Equal(ltsPreview, actual);
    }

    [Fact]
    public void GoLiveAcceptedAsPreview()
    {
        var previewRelease = new DotnetReleasesIndex.ChannelIndex
        {
            LatestRelease = "100.100.100-preview.1",
            LatestSdk = "100.100.100-preview.1",
            MajorMinorVersion = "100.100",
            ReleaseType = "lts",
            SupportPhase = "go-live",
            ChannelReleaseIndexUrl = null!
        };

        var releasesIndex = new DotnetReleasesIndex {
            Releases = [previewRelease]
        };

        var actual = releasesIndex.GetChannelIndex(Channel.Preview);
        Assert.Equal(previewRelease, actual);
    }
}