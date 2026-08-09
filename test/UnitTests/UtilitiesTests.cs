using Semver;
using Xunit;

namespace Dnvm.Test;

public sealed class UtilitiesTests
{
    [Theory]
    [InlineData("6.0.105-rc.2.21505.57", "6.0.100")]
    [InlineData("8.0.105", "8.0.100")]
    [InlineData("8.0.105-preview.1.12345", "8.0.100-preview.1")]
    [InlineData("8.0.105-rc.2.12345", "8.0.100-rc.2")]
    [InlineData("8.0.105-dev.12345", "8.0.100")]
    [InlineData("8.0.105-ci.12345", "8.0.100")]
    [InlineData("8.0.105-rtm.12345", "8.0.100")]
    public void ToFeatureBandMatchesDotnetSdk(string version, string expected)
    {
        var sdkVersion = SemVersion.Parse(version, SemVersionStyles.Strict);
        Assert.Equal(expected, sdkVersion.ToFeatureBand());
    }
}
