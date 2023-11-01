
using Dnvm;
using Dnvm.Test;
using Semver;
using Xunit;

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
        var parsed = await ManifestUtils.DeserializeNewOrOldManifest(manifest, env.DotnetFeedUrl);
        Assert.Equal("dn", parsed!.CurrentSdkDir.Name);
    });

    [Fact]
    public void ManifestV3Convert() => TestUtils.RunWithServer(async server =>
    {
        var v3 = ManifestV3.Empty
            .AddSdk(new InstalledSdkV3("1.0.0"), Channel.Latest)
            .AddSdk(new InstalledSdkV3("4.0.0-preview1")
                    { SdkDirName = new("preview") },
                    Channel.Preview);
        var v4 = await v3.Convert(server.ReleasesIndexJson);
        Assert.Equal(Channel.Latest, v4.InstalledSdkVersions[0].Channel);
        Assert.Equal(Channel.Preview, v4.InstalledSdkVersions[1].Channel);
    });
}