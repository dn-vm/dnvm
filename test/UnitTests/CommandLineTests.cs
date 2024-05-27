
using Internal.CommandLine;
using Xunit;

namespace Dnvm.Test;

public sealed class CommandLineTests
{
    [Fact]
    public void TrackMissingChannel()
    {
        Assert.Throws<ArgumentSyntaxException>(() => CommandLineArguments.Parse(handleErrors: false, [
            "track"
        ]));
    }

    [Fact]
    public void TrackMajorMinor()
    {
        var options = CommandLineArguments.Parse([
            "track",
            "99.99"
        ]);
        Assert.True(options.Command is CommandArguments.TrackArguments {
            Channel: Channel.Versioned { Major: 99, Minor: 99 }
        });
    }

    [Fact]
    public void Install()
    {
        var options = CommandLineArguments.Parse([
            "install",
            MockServer.DefaultLtsVersion.ToString()
        ]);
        Assert.True(options.Command is CommandArguments.InstallArguments {
            SdkVersion: var sdkVersion
        } && sdkVersion == MockServer.DefaultLtsVersion);
    }

    [Fact]
    public void InstallOptions()
    {
        var options = CommandLineArguments.Parse([
            "install",
            "-f",
            "--sdk-dir", "test",
            "-v",
            MockServer.DefaultLtsVersion.ToString(),
        ]);
        Assert.True(options.Command is CommandArguments.InstallArguments {
            SdkVersion: var sdkVersion,
            Force: true,
            SdkDir: {} sdkDir,
            Verbose: true
        } && sdkVersion == MockServer.DefaultLtsVersion && sdkDir.Name == "test");
    }

    [Fact]
    public void InstallBadVersion()
    {
        Assert.Throws<ArgumentSyntaxException>(() => CommandLineArguments.Parse(handleErrors: false, [
            "install",
            "badversion"
        ]));
    }

    [Fact]
    public void TrackMixedCase()
    {
        var options = CommandLineArguments.Parse([
            "track",
            "lTs"
        ]);
        Assert.True(options.Command is CommandArguments.TrackArguments {
            Channel: Channel.Lts
        });
    }
}