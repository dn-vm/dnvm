using System;
using System.Linq;
using Spectre.Console.Testing;
using Xunit;

namespace Serde.CmdLine.Test;

public sealed partial class DeserializerTests
{
    [Fact]
    public void SpectreExample()
    {
        string[] testArgs = [ "-p", "*.txt", "--hidden", ];
        var cmd = CmdLine.ParseRaw<FileSizeCommand>(testArgs);
        Assert.Equal(new FileSizeCommand { SearchPath = null, SearchPattern = "*.txt", IncludeHidden = true }, cmd);
    }

    [Fact]
    public void TestSearchPath()
    {
        string[] testArgs = [ "search-path" ];
        var cmd = CmdLine.ParseRaw<FileSizeCommand>(testArgs);
        Assert.Equal(new FileSizeCommand { SearchPath = "search-path", SearchPattern = null, IncludeHidden = null }, cmd);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void TestHelp(string arg)
    {
        string[] args = [ arg ];
        var testConsole = new TestConsole();
        Assert.True(CmdLine.TryParse<FileSizeCommand>(args, testConsole, out _));
        var text = """
Usage: FileSizeCommand [-p | --pattern <searchPattern>] [--hidden] <searchPath>

Arguments:
    <searchPath>  Path to search. Defaults to current directory.

Options:
    -p, --pattern  <searchPattern>
    --hidden

""";
        Assert.Equal(text.NormalizeLineEndings(), string.Join(Environment.NewLine, testConsole.Output));
    }

    [Fact]
    public void HelpAndBadOption()
    {
        string[] args = [ "-h", "--bad-option" ];
        var testConsole = new TestConsole();
        Assert.False(CmdLine.TryParse<FileSizeCommand>(args, testConsole, out _));
        var text = """
error: Unexpected argument: '--bad-option'
Usage: FileSizeCommand [-p | --pattern <searchPattern>] [--hidden] <searchPath>

Arguments:
    <searchPath>  Path to search. Defaults to current directory.

Options:
    -p, --pattern  <searchPattern>
    --hidden

""";
        Assert.Equal(text.NormalizeLineEndings(), string.Join(Environment.NewLine, testConsole.Output));
    }

    [Fact]
    public void BadOption()
    {
        string[] args = [ "--bad-option" ];
        var testConsole = new TestConsole();
        var ex = Assert.Throws<InvalidDeserializeValueException>(() => CmdLine.ParseRaw<FileSizeCommand>(args));
        Assert.False(CmdLine.TryParse<FileSizeCommand>(args, testConsole, out _));
        Assert.Contains(ex.Message.NormalizeLineEndings(), testConsole.Output);
    }

    [GenerateDeserialize]
    internal sealed partial record FileSizeCommand
    {
        [CommandParameter(0, "searchPath",
            Description = "Path to search. Defaults to current directory.")]
        public string? SearchPath { get; init; }

        [CommandOption("-p|--pattern")]
        public string? SearchPattern { get; init; }

        [CommandOption("--hidden")]
        public bool? IncludeHidden { get; init; }
    }

    [Fact]
    public void BasicCommandTest()
    {
        string[] cmdLine = [ "-f", "abc" ];
        var cmd = CmdLine.ParseRaw<BasicCommand>(cmdLine);
        Assert.Equal(new BasicCommand
        {
            FlagOption = true,
            Arg = "abc"
        }, cmd);
    }

    [GenerateSerde]
    private sealed partial record BasicCommand
    {
        [CommandOption("-f|--flag-option")]
        public bool? FlagOption { get; init; }

        [CommandParameter(0, "arg")]
        public required string Arg { get; init; }
    }
}
