
using Semver;
using Xunit;

namespace Dnvm.Test;

public sealed class PruneTests
{
    [Fact]
    public void OutOfDateInOneDir()
    {
        var manifest = Manifest.Empty
            .AddSdk(new(42, 42, 42), Channel.Latest, new("dn"))
            .AddSdk(new(42, 42, 43), Channel.Preview, new("dn"))
            .AddSdk(new(42, 42, 42), Channel.Preview, new("preview"));

        var outOfDate = PruneCommand.GetOutOfDateSdks(manifest);

        List<(SemVersion, SdkDirName)> expected = [ (new(42, 42, 42), new("dn")) ];
        Assert.Equal(expected, outOfDate);
    }

    [Fact]
    public void OutOfDatePreview()
    {
        var manifest = Manifest.Empty
            .AddSdk(SemVersion.Parse("8.0.0-preview.1", SemVersionStyles.Strict), Channel.Preview, new("dn"))
            .AddSdk(SemVersion.Parse("8.0.0-rc.2", SemVersionStyles.Strict), Channel.Preview, new("dn"));

        var outOfDate = PruneCommand.GetOutOfDateSdks(manifest);

        var item1 = manifest.InstalledSdkVersions[0];
        List<(SemVersion, SdkDirName)> expected = [ (item1.SdkVersion, item1.SdkDirName) ];
        Assert.Equal(expected, outOfDate);
    }
}