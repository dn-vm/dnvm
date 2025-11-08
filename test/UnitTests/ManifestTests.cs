
using Dnvm;
using Semver;
using Serde.Json;
using Xunit;

namespace Dnvm.Test;

public sealed class ManifestTests
{
    [Fact]
    public Task MissingCurrentSdkDir() => TestUtils.RunWithServer(async (server, env) =>
    {
        var manifest = """
{
    "version":3,
    "installedSdkVersions":[{"version":"42.42.42","sdkDirName":{"name":"dn"}}],
    "trackedChannels":[
        {"channelName":"latest","sdkDirName":{"name":"dn"},"installedSdkVersions":["42.42.42"]}
    ]
}
""";
        var parsed = await ManifestSerialize.DeserializeNewOrOldManifest(env.HttpClient, manifest, env.DotnetFeedUrls);
        Assert.Equal("dn", parsed!.CurrentSdkDir.Name);
    });

    [Fact]
    public Task V5CopiesChannel() => TestUtils.RunWithServer(async (server, env) =>
    {
        var manifest = """
{
    "version":3,"currentSdkDir":{"name":"dn"},
    "installedSdkVersions":[{"version":"7.0.203","sdkDirName":{"name":"dn"}},{"version":"8.0.100-preview.3.23178.7","sdkDirName":{"name":"preview"}}],
    "trackedChannels":[
        {"channelName":"latest","sdkDirName":{"name":"dn"},"installedSdkVersions":["7.0.203"]},
        {"channelName":"preview","sdkDirName":{"name":"dn"},"installedSdkVersions":["8.0.100-preview.3.23178.7"]}
    ]
}
""";
        server.ReleasesIndexJson = new()
        {
            ChannelIndices = [
                new DotnetReleasesIndex.ChannelIndex {
                    ReleaseType = "lts",
                    SupportPhase = "active",
                    MajorMinorVersion = "7.0",
                    LatestRelease = "7.0.2",
                    LatestSdk = "7.0.203",
                    ChannelReleaseIndexUrl = server.GetChannelIndexUrl("7.0")
                },
                new DotnetReleasesIndex.ChannelIndex {
                    ReleaseType = "preview",
                    SupportPhase = "active",
                    MajorMinorVersion = "8.0",
                    LatestRelease = "8.0.0-preview.3.23178.7",
                    LatestSdk = "8.0.100-preview.3.23178.7",
                    ChannelReleaseIndexUrl = server.GetChannelIndexUrl("8.0")
                }
            ]
        };
        server.ChannelIndexMap.Clear();
        var runtimeVersion = new SemVersion(7, 0, 2);
        var sdkVersion = SemVersion.Parse("7.0.203", SemVersionStyles.Strict);
        server.ChannelIndexMap.Add("7.0", new()
        {
            Releases = [
                new ChannelReleaseIndex.Release {
                    AspNetCore = new() { Version = runtimeVersion, Files = [ ] },
                    ReleaseVersion = runtimeVersion,
                    Runtime = new() { Version = runtimeVersion, Files = [ ] },
                    Sdk = new() { Version = sdkVersion, Files = [ ]},
                    Sdks = [ new() { Version = sdkVersion, Files = [ ] }  ],
                    WindowsDesktop = new() { Version = runtimeVersion, Files = [ ] },
                }
            ]
        });
        runtimeVersion = SemVersion.Parse("8.0.0-preview.3.23178.7", SemVersionStyles.Strict);
        sdkVersion = SemVersion.Parse("8.0.100-preview.3.23178.7", SemVersionStyles.Strict);
        server.ChannelIndexMap.Add("8.0", new ChannelReleaseIndex()
        {
            Releases = [
                new ChannelReleaseIndex.Release {
                    AspNetCore = new() { Version = runtimeVersion, Files = [ ] },
                    ReleaseVersion = runtimeVersion,
                    Runtime = new() { Version = runtimeVersion, Files = [ ] },
                    Sdk = new() { Version = sdkVersion, Files = [ ] },
                    Sdks = [ new() { Version = sdkVersion, Files = [ ] }  ],
                    WindowsDesktop = new() { Version = runtimeVersion, Files = [ ] },
                }
            ]
        });

        var v5 = (await ManifestSerialize.DeserializeNewOrOldManifest(env.HttpClient, manifest, env.DotnetFeedUrls))!;
        Assert.Equal(new Channel.Latest(), v5.RegisteredChannels.Single(c => c.InstalledSdkVersions.Contains(v5.InstalledSdks[0].SdkVersion)).ChannelName);
        Assert.Equal(new Channel.Preview(), v5.RegisteredChannels.Single(c => c.InstalledSdkVersions.Contains(v5.InstalledSdks[1].SdkVersion)).ChannelName);
    });

    [Fact]
    public Task ManifestV3Convert() => TestUtils.RunWithServer(async (server, env) =>
    {
        var v3 = ManifestV3.Empty
            .AddSdk(new InstalledSdkV3 { Version = "42.42.42", SdkDirName = new(DnvmEnv.DefaultSdkDirName.Name) }, new Channel.Latest())
            .AddSdk(new InstalledSdkV3 { Version = "99.99.99-preview", SdkDirName = new("preview") },
                    new Channel.Preview());
        var v5 = await v3.Convert().Convert(env.HttpClient, server.ReleasesIndexJson);
        Assert.Equal(new Channel.Latest(), v5.InstalledSdkVersions[0].Channel);
        Assert.Equal(new Channel.Preview(), v5.InstalledSdkVersions[1].Channel);
    });

    [Fact]
    public void WriteManifestV9()
    {
        var manifest = new Manifest
        {
            InstalledSdks = [
                new InstalledSdk
                {
                    ReleaseVersion = new SemVersion(7, 0, 2),
                    SdkVersion = new SemVersion(7, 0, 203),
                    RuntimeVersion = new SemVersion(7, 0, 2),
                    AspNetVersion = new SemVersion(7, 0, 2),
                    SdkDirName = new SdkDirName("dn")
                }
            ],
            RegisteredChannels = [
                new RegisteredChannel
                {
                    ChannelName = new Channel.Latest(),
                    SdkDirName = new SdkDirName("dn"),
                    InstalledSdkVersions = [ new SemVersion(7, 0, 203) ]
                },
                new RegisteredChannel
                {
                    ChannelName = new Channel.Preview(),
                    SdkDirName = new SdkDirName("preview"),
                    InstalledSdkVersions = [ SemVersion.Parse("8.0.100-preview.3.23178.7", SemVersionStyles.Strict) ]
                }
            ],
            CurrentSdkDir = new SdkDirName("dn")
        };
        var expected = """
{
    "version":9,
    "previewsEnabled": false,
    "currentSdkDir": "dn",
    "installedSdks":[
        {
            "releaseVersion":"7.0.2",
            "sdkVersion":"7.0.203",
            "runtimeVersion":"7.0.2",
            "aspNetVersion":"7.0.2",
            "sdkDirName": "dn"
        }
    ],
    "registeredChannels":[
        {
            "channelName":"latest",
            "sdkDirName": "dn",
            "installedSdkVersions":["7.0.203"],
            "untracked": false
        },
        {
            "channelName":"preview",
            "sdkDirName": "preview",
            "installedSdkVersions":["8.0.100-preview.3.23178.7"],
            "untracked": false
        }
    ]
}
""";
        var serialized = ManifestSerialize.Serialize(manifest);
        Assert.Equal(JsonSerializer.DeserializeJsonValue(expected), JsonSerializer.DeserializeJsonValue(serialized));
    }
}