
using Dnvm;
using Xunit;

public sealed class ManifestTests
{
    [Fact]
    public void MissingCurrentSdkDir()
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
        var parsed = ManifestUtils.DeserializeNewOrOldManifest(manifest)!;
        Assert.Equal("dn", parsed.CurrentSdkDir.Name);
    }
}