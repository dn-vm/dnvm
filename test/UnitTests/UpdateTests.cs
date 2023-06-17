
using System.Collections.Immutable;
using System.Runtime.InteropServices.Marshalling;
using Semver;
using Serde.Json;
using Spectre.Console.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class UpdateTests : IDisposable
{
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;
    private readonly Logger _logger;

    public UpdateTests(ITestOutputHelper output)
    {
        var wrapper = new OutputWrapper(output);
        _logger = new Logger(new TestConsole());
        _globalOptions = new GlobalOptions() {
            DnvmHome = _dnvmHome.Path,
            UserHome = _userHome.Path,
            GetUserEnvVar = s => _envVars[s],
            SetUserEnvVar = (name, val) => _envVars[name] = val,
        };
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
    }

    private ValueTask TestWithServer(Action<MockServer, CommandArguments.UpdateArguments, CancellationToken> test)
        => TestWithServer((mockServer, updateArguments, cancellationToken) => {
            test(mockServer, updateArguments, cancellationToken);
            return ValueTask.CompletedTask;
        });

    private ValueTask TestWithServer(Func<MockServer, CommandArguments.UpdateArguments, CancellationToken, ValueTask> test)
    {
        return TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            var updateArguments = new CommandArguments.UpdateArguments() {
                FeedUrl = mockServer.PrefixString,
                DnvmReleasesUrl = mockServer.DnvmReleasesUrl,
                Verbose = true,
                Yes = true,
            };
            await test(mockServer, updateArguments, taskScope.CancellationToken);
        });
    }

    [Fact]
    public ValueTask UpdateChecksForSelfUpdate()
    {
        return TestWithServer(async (mockServer, updateArguments, cancellationToken) =>
        {
            var sdkDir = GlobalOptions.DefaultSdkDirName;
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
                _dnvmHome.Path,
                logger,
                releasesIndex,
                manifest,
                yes: false,
                updateArguments.FeedUrl!,
                updateArguments.DnvmReleasesUrl!,
                _globalOptions.ManifestPath,
                cancellationToken);
            Assert.Contains("dnvm is out of date", console.Output);
        });
    }

    [Fact]
    public ValueTask FindsNewerLatestToLtsVersion() => TestWithServer((mockServer, _, _) =>
    {
        // Construct a manifest with an installed version "41.0.0" in the Latest channel
        // and confirm that 42.42 is processed as newer
        var installedVersion = "41.0.0";
        var manifest = new Manifest {
            InstalledSdkVersions = ImmutableArray.Create(new InstalledSdk { Version = installedVersion, SdkDirName = GlobalOptions.DefaultSdkDirName }),
            TrackedChannels = ImmutableArray.Create(new TrackedChannel {
                ChannelName = Channel.Latest,
                SdkDirName = GlobalOptions.DefaultSdkDirName,
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
    });

    [Fact]
    public async ValueTask InstallAndUpdate() => await TestWithServer(async (mockServer, updateArguments, cancellationToken) =>
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
        var result = await InstallCommand.Run(_globalOptions, _logger, new() {
            Channel = channel,
            FeedUrl = mockServer.PrefixString,
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
        var updateResult = await UpdateCommand.Run(_globalOptions, _logger, updateArguments);
        var sdkVersions = ImmutableArray.Create(new[] { "41.0.100", "41.0.101" });
        Assert.Equal(UpdateCommand.Result.Success, updateResult);
        var expectedManifest = new Manifest {
            InstalledSdkVersions = sdkVersions.Select(v => new InstalledSdk { Version = v, SdkDirName = GlobalOptions.DefaultSdkDirName }).ToImmutableArray(),
            TrackedChannels = ImmutableArray.Create(new[] { new TrackedChannel() {
                ChannelName = channel,
                SdkDirName = GlobalOptions.DefaultSdkDirName,
                InstalledSdkVersions = sdkVersions
            }})
        };
        var actualManifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(_globalOptions.ManifestPath));
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