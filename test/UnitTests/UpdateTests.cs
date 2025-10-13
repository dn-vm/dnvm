using System.Collections.Immutable;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestPlatform.CoreUtilities.Helpers;
using Semver;
using Serde.Json;
using Spectre.Console.Testing;
using Xunit;

namespace Dnvm.Test;

public sealed class UpdateTests
{
    private readonly Logger _logger;
    private readonly DnvmSubCommand.UpdateArgs updateArguments = new() {
        Verbose = true,
        Yes = true,
    };

    public UpdateTests()
    {
        _logger = new Logger(new StringWriter());
    }

    private static Task TestWithServer(Func<MockServer, DnvmEnv, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl);
            await test(mockServer, testOptions.DnvmEnv);
        });

    [Fact]
    public Task UpdateChecksForSelfUpdate() => TestWithServer(async (mockServer, env) =>
    {
        var sdkDir = DnvmEnv.DefaultSdkDirName;
        var @lock = await ManifestLock.Acquire(env);
        var manifest = new Manifest
        {
            InstalledSdks = [ new InstalledSdk {
                SdkVersion = new(42, 42, 142),
                AspNetVersion = new(42, 42, 142),
                RuntimeVersion = new(42, 42, 142),
                ReleaseVersion = new(42, 42, 142),
                SdkDirName = sdkDir
            } ],
            RegisteredChannels =
            [
                new RegisteredChannel
                    {
                        ChannelName = new Channel.Latest(),
                        SdkDirName = sdkDir,
                        InstalledSdkVersions = [ new(42, 42, 142) ]
                    },
            ]
        };
        var releasesIndex = mockServer.ReleasesIndexJson;
        _ = await UpdateCommand.UpdateSdks(
            env,
            _logger,
            releasesIndex,
            @lock,
            manifest,
            yes: false,
            env.DnvmReleasesUrl!);
        Assert.Contains("dnvm is out of date", ((TestConsole)env.Console).Output);
    });

    [Fact]
    public Task FindsNewerLatestToLtsVersion() => TestUtils.RunWithServer(mockServer =>
    {
        // Construct a manifest with an installed version "41.0.0" in the Latest channel
        // and confirm that 42.42 is processed as newer
        var installedVersion = new SemVersion(41, 0, 0);
        var manifest = new Manifest {
            InstalledSdks = [ new InstalledSdk {
                SdkVersion = installedVersion,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                AspNetVersion = installedVersion,
                RuntimeVersion = installedVersion,
                ReleaseVersion = installedVersion,
            }] ,
            RegisteredChannels = [ new RegisteredChannel {
                ChannelName = new Channel.Latest(),
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = [ installedVersion ]
            } ]
        };
        var releasesIndex = mockServer.ReleasesIndexJson;
        var results = UpdateCommand.FindPotentialUpdates(manifest, releasesIndex);
        var (channel, newestInstalled, newestAvailable, sdkDir) = results[0];
        Assert.Equal(new Channel.Latest(), channel);
        Assert.Equal(new SemVersion(41, 0, 0), newestInstalled);
        Assert.Equal("42.42.42", newestAvailable!.LatestRelease);
        Assert.Equal(DnvmEnv.DefaultSdkDirName, sdkDir);
        Assert.Single(results);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task InstallAndUpdate() => await TestWithServer(async (mockServer, env) =>
    {
        var baseVersion = new SemVersion(41, 0, 0);
        var upgradeVersion = new SemVersion(41, 0, 1);
        Channel channel = new Channel.Latest();
        Setup(mockServer, baseVersion);
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options() {
            Channel = channel,
            Verbose = true
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        // Update with a newer version
        Setup(mockServer, upgradeVersion);
        var updateResult = await UpdateCommand.Run(env, _logger, updateArguments);
        EqArray<SemVersion> sdkVersions = [ baseVersion, upgradeVersion ];
        Assert.Equal(UpdateCommand.Result.Success, updateResult);
        var expectedManifest = new Manifest {
            InstalledSdks = sdkVersions.Select(v => new InstalledSdk
            {
                SdkVersion = v,
                AspNetVersion = v,
                RuntimeVersion = v,
                ReleaseVersion = v,
                SdkDirName = DnvmEnv.DefaultSdkDirName }).ToEq(),
            RegisteredChannels = [ new RegisteredChannel() {
                ChannelName = channel,
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = sdkVersions
            } ]
        };
        var actualManifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal(expectedManifest, actualManifest);

        static void Setup(MockServer mockServer, SemVersion version)
        {
            mockServer.ReleasesIndexJson = new()
            {
                ChannelIndices = [ new() {
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
                Releases = [ TestUtils.CreateRelease(mockServer.PrefixString, version)]
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
            ChannelIndices = [ltsRelease, stsRelease, ltsPreview, stsPreview]
        };

        var actual = releasesIndex.GetChannelIndex(new Channel.Latest());
        // STS is newest
        Assert.Equal(stsRelease, actual);

        actual = releasesIndex.GetChannelIndex(new Channel.Lts());
        Assert.Equal(ltsRelease, actual);

        actual = releasesIndex.GetChannelIndex(new Channel.Sts());
        Assert.Equal(stsRelease, actual);

        actual = releasesIndex.GetChannelIndex(new Channel.Preview());
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
            ChannelIndices = [previewRelease]
        };

        var actual = releasesIndex.GetChannelIndex(new Channel.Preview());
        Assert.Equal(previewRelease, actual);
    }

    [Fact]
    public Task DontUpdateUntracked() => TestUtils.RunWithServer(async (server, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options() {
            Channel = new Channel.Latest(),
            Verbose = true
        });

        Assert.Equal(TrackCommand.Result.Success, result);

        result = await TrackCommand.Run(env, _logger, new TrackCommand.Options() {
            Channel = new Channel.Preview(),
            Verbose = true
        });
        Assert.Equal(TrackCommand.Result.Success, result);
    });

    [Fact]
    public Task ChannelOverlap() => TestUtils.RunWithServer(async (server, env) =>
    {
        // Default release index only contains an LTS release, so adding LTS and latest
        // should result in the same SDK being installed
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options() { Channel = new Channel.Latest() });
        Assert.Equal(TrackCommand.Result.Success , result);
        result = await TrackCommand.Run(env, _logger, new TrackCommand.Options() { Channel = new Channel.Lts() });
        Assert.Equal(TrackCommand.Result.Success , result);

        var oldRelease = server.ReleasesIndexJson.ChannelIndices.Single(r => r.ReleaseType == "lts");
        var newSdkVersion = new SemVersion(42, 42, 43);
        var newRelease = server.RegisterReleaseVersion(newSdkVersion, "lts", "active");
        var updateResult = await UpdateCommand.Run(env, _logger, new UpdateCommand.Options() { Yes = true });

        var oldSdkVersion = SemVersion.Parse(oldRelease.LatestSdk, SemVersionStyles.Strict);
        var oldReleaseVersion = SemVersion.Parse(oldRelease.LatestRelease, SemVersionStyles.Strict);
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal([
            new() {
                SdkVersion = oldSdkVersion,
                ReleaseVersion = oldReleaseVersion,
                RuntimeVersion = oldReleaseVersion,
                AspNetVersion = oldReleaseVersion,
            },
            new() {
                SdkVersion = newSdkVersion,
                ReleaseVersion = newSdkVersion,
                RuntimeVersion = newSdkVersion,
                AspNetVersion = newSdkVersion,
            } ], manifest.InstalledSdks);
        Assert.Equal([
            new() {
                ChannelName = new Channel.Latest(),
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = [ oldSdkVersion, newSdkVersion ]
            },
            new() {
                ChannelName = new Channel.Lts(),
                SdkDirName = DnvmEnv.DefaultSdkDirName,
                InstalledSdkVersions = [ oldSdkVersion, newSdkVersion ]
            }
        ], manifest.RegisteredChannels);
    });

    [Fact]
    public async Task UpdateNoBuilds() => await TestUtils.RunWithServer(async (server, env) =>
    {
        server.ReleasesIndexJson = DotnetReleasesIndex.Empty;
        server.ChannelIndexMap.Clear();
        server.RegisterReleaseVersion(MockServer.DefaultLtsVersion, "lts", "active");
        var trackResult = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Preview()
        });
        Assert.Equal(TrackCommand.Result.Success, trackResult);
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal([ new RegisteredChannel {
            ChannelName = new Channel.Preview(),
            SdkDirName = DnvmEnv.DefaultSdkDirName,
            InstalledSdkVersions = [ ]
        }], manifest.RegisteredChannels);

        var updateResult = await UpdateCommand.Run(env, _logger, updateArguments);
        Assert.Equal(UpdateCommand.Result.Success, updateResult);
    });

    [Fact]
    public async Task UpdateNoBuildsInstalled() => await TestUtils.RunWithServer(async (server, env) =>
    {
        server.ReleasesIndexJson = DotnetReleasesIndex.Empty;
        server.ChannelIndexMap.Clear();
        server.RegisterReleaseVersion(MockServer.DefaultLtsVersion, "lts", "active");
        var trackResult = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Preview()
        });
        Assert.Equal(TrackCommand.Result.Success, trackResult);
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal([ new RegisteredChannel {
            ChannelName = new Channel.Preview(),
            SdkDirName = DnvmEnv.DefaultSdkDirName,
            InstalledSdkVersions = [ ]
        }], manifest.RegisteredChannels);

        server.RegisterReleaseVersion(MockServer.DefaultPreviewVersion, "sts", "preview");
        var updateResult = await UpdateCommand.Run(env, _logger, updateArguments);
        Assert.Equal(UpdateCommand.Result.Success, updateResult);
        manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal([ new RegisteredChannel {
            ChannelName = new Channel.Preview(),
            SdkDirName = DnvmEnv.DefaultSdkDirName,
            InstalledSdkVersions = [ MockServer.DefaultPreviewVersion ]
        }], manifest.RegisteredChannels);
    });

    [Fact]
    public async Task MultipleTracked() => await TestUtils.RunWithServer(async (mockServer, env) =>
    {
        mockServer.ClearVersions();

        var initialVersion = new SemVersion(6, 0, 0);
        mockServer.RegisterReleaseVersion(initialVersion, "lts", "active");
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Lts(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        // Explicitly add the LTS channel to the manifest since it's impossible to create this
        // manifest through the command line
        var manifest = await Manifest.ReadManifestUnsafe(env);
        manifest = manifest.TrackChannel(new RegisteredChannel
        {
            ChannelName = new Channel.Lts(),
            SdkDirName = new("custom-sdk-dir"),
        });
        await Manifest.WriteManifestUnsafe(env, manifest);
        var updatedVersion = new SemVersion(6, 0, 1);
        mockServer.RegisterReleaseVersion(updatedVersion, "lts", "active");

        var updateResult = await UpdateCommand.Run(env, _logger, new UpdateCommand.Options
        {
            Yes = true,
        });
        Assert.Equal(UpdateCommand.Result.Success, updateResult);
        manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal([
            new InstalledSdk
            {
                SdkVersion = initialVersion,
                ReleaseVersion = initialVersion,
                RuntimeVersion = initialVersion,
                AspNetVersion = initialVersion,
                SdkDirName = DnvmEnv.DefaultSdkDirName
            },
            new InstalledSdk
            {
                SdkVersion = updatedVersion,
                ReleaseVersion = updatedVersion,
                RuntimeVersion = updatedVersion,
                AspNetVersion = updatedVersion,
                SdkDirName = DnvmEnv.DefaultSdkDirName
            },
            new InstalledSdk
            {
                SdkVersion = updatedVersion,
                ReleaseVersion = updatedVersion,
                RuntimeVersion = updatedVersion,
                AspNetVersion = updatedVersion,
                SdkDirName = new("custom-sdk-dir")
            }
        ], manifest.InstalledSdks);
    });

    [Fact]
    public async Task UpdateAfterUninstall() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var trackResult = await TrackCommand.Run(env, _logger, new TrackCommand.Options() {
            Channel = new Channel.Lts(),
            Verbose = true
        });
        Assert.Equal(TrackCommand.Result.Success, trackResult);
        var manifest = await Manifest.ReadManifestUnsafe(env);
        var installed = Assert.Single(manifest.InstalledSdks);
        Assert.Equal(MockServer.DefaultLtsVersion, installed.SdkVersion);

        var uninstallResult = await UninstallCommand.Run(env, _logger, MockServer.DefaultLtsVersion);
        Assert.Equal(0, uninstallResult);

        manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Empty(manifest.InstalledSdks);

        var updateResult = await UpdateCommand.Run(env, _logger, new UpdateCommand.Options
        {
            Yes = true,
        });

        Assert.Equal(UpdateCommand.Result.Success, updateResult);

        manifest = await Manifest.ReadManifestUnsafe(env);
        installed = Assert.Single(manifest.InstalledSdks);
        Assert.Equal(MockServer.DefaultLtsVersion, installed.SdkVersion);
    });
}