
using Dnvm;
using Semver;
using Xunit;

public sealed class ManifestTests
{
    [Fact]
    public async Task MissingCurrentSdkDir()
    {
        var manifest = """
{
    "version":3,
    "installedSdkVersions":[{"version":"7.0.102","sdkDirName":{"name":"dn"}}],
    "trackedChannels":[
        {"channelName":"latest","sdkDirName":{"name":"dn"},"installedSdkVersions":["7.0.102"]}
    ]
}
""";
   //     var parsed = await ManifestUtils.DeserializeNewOrOldManifest(manifest)!;
   //     Assert.Equal("dn", parsed.CurrentSdkDir.Name);
    }

    [Fact]
    public void ManifestV3Convert()
    {
        var v3 = ManifestV3.Empty
            .AddSdk(new InstalledSdkV3("1.0.0"), Channel.Latest)
            .AddSdk(new InstalledSdkV3("4.0.0-preview1")
                    { SdkDirName = new("preview") },
                    Channel.Preview);
        //var v4 = v3.Convert();
        //Assert.Equal(Channel.Latest, v4.InstalledSdkVersions[0].Channel);
        //Assert.Equal(Channel.Preview, v4.InstalledSdkVersions[1].Channel);
    }
}