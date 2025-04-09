
using Serde;
using Serde.CmdLine;
using Spectre.Console.Testing;
using Xunit;

namespace Dnvm.Test;

public sealed class CommandLineTests
{
    private static readonly string ExpectedHelpText = """
        usage: dnvm [--enable-dnvm-previews] [-h | --help] <command>

        Install and manage .NET SDKs.

        Options:
            --enable-dnvm-previews  Enable dnvm previews.
            -h, --help  Show help information.

        Commands:
            install  Install an SDK.
            track  Start tracking a new channel.
            selfinstall  Install dnvm to the local machine.
            update  Update the installed SDKs or dnvm itself.
            list  List installed SDKs.
            select  Select the active SDK directory.
            untrack  Remove a channel from the list of tracked channels.
            uninstall  Uninstall an SDK.
            prune  Remove all SDKs with older patch versions.
            restore  Restore the SDK listed in the global.json file.


        """.NormalizeLineEndings();

    [Fact]
    public void NoCommand()
    {
        var console = new TestConsole();
        var options = CommandLineArguments.ParseRaw(console, []);
        Assert.NotNull(options);
        Assert.Null(options.SubCommand);
        Assert.Equal(ExpectedHelpText, console.Output);
    }

    [Fact]
    public void Restore()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "restore"
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.RestoreArgs);
    }

    [Fact]
    public void List()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "list"
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.ListArgs);
    }

    [Fact]
    public void SelectWithDir()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "select",
            "preview"
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.SelectArgs {
            SdkDirName: "preview"
        });
    }

    [Fact]
    public void TrackMissingChannel()
    {
        Assert.Throws<ArgumentSyntaxException>(() => CommandLineArguments.ParseRaw(new TestConsole(), [
            "track"
        ]));
    }

    [Fact]
    public void TrackMajorMinor()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "track",
            "99.99"
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.TrackArgs {
            Channel: Channel.VersionedMajorMinor { Major: 99, Minor: 99 }
        });
    }

    [Fact]
    public void TrackFeature()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "track",
            "99.99.9xx"
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.TrackArgs {
            Channel: Channel.VersionedFeature { Major: 99, Minor: 99, FeatureLevel: 9 }
        });
    }

    [Theory]
    [InlineData("badchannel")]
    [InlineData("99.99.9x")]
    [InlineData("99.99.9xxx")]
    [InlineData("99.99.900")]
    [InlineData("99.99.axx")]
    public void TrackBadChannel(string channel)
    {
        var ex = Assert.Throws<ArgumentSyntaxException>(() => CommandLineArguments.ParseRaw(new TestConsole(), [
            "track",
            channel
        ]));
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
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "install",
            MockServer.DefaultLtsVersion.ToString(),
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.InstallArgs {
            SdkVersion: var sdkVersion
        } && sdkVersion == MockServer.DefaultLtsVersion);
    }

    [Fact]
    public void InstallOptions()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "install",
            "-f",
            "--sdk-dir", "test",
            "-v",
            MockServer.DefaultLtsVersion.ToString(),
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.InstallArgs {
            SdkVersion: var sdkVersion,
            Force: true,
            SdkDir: {} sdkDir,
            Verbose: true
        } && sdkVersion == MockServer.DefaultLtsVersion && sdkDir.Name == "test");
    }

    [Fact]
    public void InstallBadVersion()
    {
        Assert.Throws<ArgumentSyntaxException>(() => CommandLineArguments.ParseRaw(new TestConsole(), [
            "install",
            "badversion"
        ]));
    }

    [Fact]
    public void TrackMixedCase()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "track",
            "lTs"
        ]);
        Assert.True(options!.SubCommand is DnvmSubCommand.TrackArgs {
            Channel: Channel.Lts
        });
    }

    [Fact]
    public void EnableDnvmPreviews()
    {
        var options = CommandLineArguments.ParseRaw(new TestConsole(), [
            "--enable-dnvm-previews",
        ]);
        Assert.True(options!.EnableDnvmPreviews);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void RunHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(console, [ param ]));
        Assert.Equal(ExpectedHelpText, console.Output);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void ListHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "list", param ]));
        Assert.Equal("""
usage: dnvm list [-h | --help]

List installed SDKs.

Options:
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void SelectHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "select", param ]));
        Assert.Equal("""
usage: dnvm select [-h | --help] <sdkDirName>

Select the active SDK directory, meaning the directory that will be used when
running `dotnet` commands. This is the same directory passed to the `-s` option
for `dnvm install`.

Note: This command does not change between SDK versions installed in the same
directory. For that, use the built-in dotnet global.json file. Information about
global.json can be found at
https://learn.microsoft.com/en-us/dotnet/core/tools/global-json.

Arguments:
    <sdkDirName>  The name of the SDK directory to select.

Options:
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void InstallHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "install", param ]));
        Assert.Equal("""
usage: dnvm install [-f | --force] [-s | --sdk-dir <sdkDir>] [-v | --verbose]
[-h | --help] <version>

Install an SDK.

Arguments:
    <version>  The version of the SDK to install.

Options:
    -f, --force  Force install the given SDK, even if already installed
    -s, --sdk-dir  <sdkDir>  Install the SDK into a separate directory with the
given name.
    -v, --verbose  Print debugging messages to the console.
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void TrackHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "track", param ]));
        Assert.Equal("""
usage: dnvm track [--feed-url <feedUrl>] [-v | --verbose] [-f | --force] [-y]
[--prereqs] [-s | --sdk-dir <sdkDir>] [-h | --help] <channel>

Start tracking a new channel.

Arguments:
    <channel>  Track the channel specified.

Options:
    --feed-url  <feedUrl>  Set the feed URL to download the SDK from.
    -v, --verbose  Print debugging messages to the console.
    -f, --force  Force tracking the given channel, even if already tracked.
    -y  Answer yes to all prompts.
    --prereqs  Print prereqs for dotnet on Ubuntu.
    -s, --sdk-dir  <sdkDir>  Track the channel in a separate directory with the
given name.
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void SelfInstallHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "selfinstall", param ]));
        Assert.Equal("""
usage: dnvm selfinstall [-v | --verbose] [-f | --force] [--feed-url <feedUrl>]
[-y] [--update] [--dest-path <destPath>] [-h | --help]

Install dnvm to the local machine.

Options:
    -v, --verbose  Print debugging messages to the console.
    -f, --force  Force install the given SDK, even if already installed
    --feed-url  <feedUrl>  Set the feed URL to download the SDK from.
    -y  Answer yes to all prompts.
    --update  [internal] Update the current dnvm installation. Only intended to
be called from dnvm.
    --dest-path  <destPath>  Set the destination path for the dnvm executable.
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void UpdateHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "update", param ]));
        Assert.Equal("""
usage: dnvm update [--dnvm-url <dnvmReleasesUrl>] [--feed-url <feedUrl>] [-v |
--verbose] [--self] [-y] [-h | --help]

Update the installed SDKs or dnvm itself.

Options:
    --dnvm-url  <dnvmReleasesUrl>  Set the URL for the dnvm releases endpoint.
    --feed-url  <feedUrl>  Set the feed URL to download the SDK from.
    -v, --verbose  Print debugging messages to the console.
    --self  Update dnvm itself in the current location.
    -y  Answer yes to all prompts.
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void UninstallHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "uninstall", param ]));
        Assert.Equal("""
usage: dnvm uninstall [-s | --sdk-dir <sdkDir>] [-h | --help] <sdkVersion>

Uninstall an SDK.

Arguments:
    <sdkVersion>  The version of the SDK to uninstall.

Options:
    -s, --sdk-dir  <sdkDir>  Uninstall the SDK from the given directory.
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void PruneHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "prune", param ]));
        Assert.Equal("""
usage: dnvm prune [-v | --verbose] [--dry-run] [-h | --help]

Remove all SDKs with older patch versions.

Options:
    -v, --verbose  Print extra debugging info to the console.
    --dry-run  Print the list of the SDKs to be uninstalled, but don't
uninstall.
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void UntrackHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "untrack", param ]));
        Assert.Equal("""
usage: dnvm untrack [-h | --help] <channel>

Remove a channel from the list of tracked channels.

Arguments:
    <channel>  The channel to untrack.

Options:
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void RestoreHelp(string param)
    {
        var console = new TestConsole();
        Assert.Null(CommandLineArguments.ParseRaw(
            console,
            [ "restore", param ]));
        Assert.Equal("""
usage: dnvm restore [-h | --help]

Downloads the SDK in the global.json in or above the current directory to the
.dotnet folder in the same directory.

Options:
    -h, --help  Show help information.


""".NormalizeLineEndings(), console.Output.TrimLines());
    }
}
