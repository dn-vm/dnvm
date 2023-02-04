
using System.Collections.Immutable;
using Semver;
using Serde.Json;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class UpdateTests : IAsyncLifetime
{
    private readonly MockServer _mockServer = new MockServer();
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;
    private readonly Logger _logger;
    private readonly CommandArguments.UpdateArguments _updateArguments;

    public UpdateTests(ITestOutputHelper output)
    {
        var wrapper = new OutputWrapper(output);
        _logger = new Logger(wrapper, wrapper);
        _globalOptions = new GlobalOptions() {
            DnvmHome = _dnvmHome.Path,
            UserHome = _userHome.Path,
            GetUserEnvVar = s => _envVars[s],
            SetUserEnvVar = (name, val) => _envVars[name] = val,
        };
        _updateArguments = new() {
            FeedUrl = _mockServer.PrefixString,
            DnvmReleasesUrl = _mockServer.DnvmReleasesUrl,
            Verbose = true,
            Yes = true,
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _mockServer.DisposeAsync();
        _userHome.Dispose();
        _dnvmHome.Dispose();
    }

    [Fact]
    public async Task UpdateChecksForSelfUpdate()
    {
        var manifest = new Manifest {
            InstalledSdkVersions = ImmutableArray.Create("42.42.142"),
            TrackedChannels = ImmutableArray.Create(new TrackedChannel {
                ChannelName = Channel.Latest,
                InstalledSdkVersions = ImmutableArray.Create("42.42.142")
            })
        };
        var releasesIndex = _mockServer.ReleasesIndexJson;
        var writer = new StringWriter();
        var logger = new Logger(writer, writer);
        _ = await Update.UpdateSdks(
            logger,
            releasesIndex,
            manifest,
            yes: false,
            _updateArguments.FeedUrl!,
            _updateArguments.DnvmReleasesUrl!,
            _globalOptions.ManifestPath,
            _globalOptions.SdkInstallDir);
        Assert.Contains("dnvm is out of date", writer.ToString());
    }

    [Fact]
    public void FindsNewerLatestToLtsVersion()
    {
        // Construct a manifest with an installed version "41.0.0" in the Latest channel
        // and confirm that 42.42 is processed as newer
        var manifest = new Manifest {
            InstalledSdkVersions = ImmutableArray.Create("41.0.0"),
            TrackedChannels = ImmutableArray.Create(new TrackedChannel {
                ChannelName = Channel.Latest,
                InstalledSdkVersions = ImmutableArray.Create("41.0.0")
            })
        };
        var releasesIndex = _mockServer.ReleasesIndexJson;
        var results = Update.FindPotentialUpdates(manifest, releasesIndex);
        var (channel, newestInstalled, newestAvailable) = results[0];
        Assert.Equal(Channel.Latest, channel);
        Assert.Equal(new SemVersion(41, 0, 0), newestInstalled);
        Assert.Equal("42.42.42", newestAvailable!.LatestRelease);
        Assert.Single(results);
    }

    [Fact]
    public async Task InstallAndUpdate()
    {
        const Channel channel = Channel.Latest;
        _mockServer.ReleasesIndexJson = new() {
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
        var result = await Install.Run(_globalOptions, _logger, new() {
            Channel = channel,
            FeedUrl = _mockServer.PrefixString,
            Verbose = true
        });
        Assert.Equal(Install.Result.Success, result);
        // Update with a newer version
        _mockServer.ReleasesIndexJson = new() {
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
        var updateResult = await Update.Run(_globalOptions, _logger, _updateArguments);
        var sdkVersions = ImmutableArray.Create(new[] { "41.0.100", "41.0.101" });
        Assert.Equal(Update.Result.Success, updateResult);
        var expectedManifest = new Manifest {
            InstalledSdkVersions = sdkVersions,
            TrackedChannels = ImmutableArray.Create(new[] { new TrackedChannel() {
                ChannelName = channel,
                InstalledSdkVersions = sdkVersions
            }})
        };
        var actualManifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(_globalOptions.ManifestPath));
        Assert.Equal(expectedManifest, actualManifest);
    }

    [Fact]
    public void SemVerIsParsable()
    {
        Assert.NotNull(Program.SemVer); // Should throw if version is not parsable.
    }
}