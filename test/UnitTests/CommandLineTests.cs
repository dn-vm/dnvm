
using Serde;
using Spectre.Console.Testing;
using Xunit;

namespace Dnvm.Test;

public sealed class CommandLineTests
{
    [Fact]
    public void List()
    {
        var options = CommandLineArguments.Parse([
            "list"
        ], useSerdeCmdLine: true);
        Assert.True(options.Command is CommandArguments.ListArguments);
    }

    [Fact]
    public void SelectWithDir()
    {
        var options = CommandLineArguments.Parse([
            "select",
            "preview"
        ], useSerdeCmdLine: true);
        Assert.True(options.Command is CommandArguments.SelectArguments {
            SdkDirName: "preview"
        });
    }

    [Fact]
    public void TrackMissingChannel()
    {
        Assert.Throws<InvalidDeserializeValueException>(() => CommandLineArguments.Parse(new TestConsole(), handleErrors: false, [
            "track"
        ], useSerdeCmdLine: true));
    }

    [Fact]
    public void TrackMajorMinor()
    {
        var options = CommandLineArguments.Parse([
            "track",
            "99.99"
        ], useSerdeCmdLine: true);
        Assert.True(options.Command is CommandArguments.TrackArguments {
            Channel: Channel.Versioned { Major: 99, Minor: 99 }
        });
    }

    [Fact]
    public void TrackBadChannel()
    {
        var ex = Assert.Throws<FormatException>(() => CommandLineArguments.Parse(new TestConsole(), handleErrors: false, [
            "track",
            "badversion"
        ], useSerdeCmdLine: true));
        var tab = "\t";
        Assert.Equal($"""
Channel must be one of:
{tab}- Latest
{tab}- Preview
{tab}- LTS
{tab}- STS
""", ex.Message);
    }

    [Fact]
    public void Install()
    {
        var options = CommandLineArguments.Parse([
            "install",
            MockServer.DefaultLtsVersion.ToString(),
        ], useSerdeCmdLine: true);
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
        Assert.Throws<InvalidDeserializeValueException>(() => CommandLineArguments.Parse(new TestConsole(), handleErrors: false, [
            "install",
            "badversion"
        ], useSerdeCmdLine: true));
    }

    [Fact]
    public void TrackMixedCase()
    {
        var options = CommandLineArguments.Parse([
            "track",
            "lTs"
        ], useSerdeCmdLine: true);
        Assert.True(options.Command is CommandArguments.TrackArguments {
            Channel: Channel.Lts
        });
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void RunHelp(string param)
    {
        var console = new TestConsole();
        Assert.Throws<Serde.CmdLine.HelpRequestedException>(() => CommandLineArguments.Parse(console, handleErrors: false, [ param ], useSerdeCmdLine: true));
        Assert.Equal("""
Usage: dnvm <command>

Install and manage .NET SDKs.

Commands:
    list  List installed SDKs
    select  Select the active SDK directory
    install  Install an SDK

""".NormalizeLineEndings(), console.Output);
    }
}