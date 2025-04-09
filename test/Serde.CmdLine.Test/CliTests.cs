using System;
using System.Collections.Generic;
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
        var cmd = CmdLine.ParseRawWithHelp<FileSizeCommand>(testArgs).Unwrap();
        Assert.Equal(new FileSizeCommand { SearchPath = null, SearchPattern = "*.txt", IncludeHidden = true }, cmd);
    }

    [Fact]
    public void TestSearchPath()
    {
        string[] testArgs = [ "search-path" ];
        var cmd = CmdLine.ParseRawWithHelp<FileSizeCommand>(testArgs).Unwrap();
        Assert.Equal(new FileSizeCommand { SearchPath = "search-path", SearchPattern = null, IncludeHidden = null }, cmd);
    }

    [Fact]
    public void TestHelp()
    {
        var help = CmdLine.GetHelpText(SerdeInfoProvider.GetDeserializeInfo<FileSizeCommand>());
        var text = """
usage: FileSizeCommand [-p | --pattern <searchPattern>] [--hidden] [-h | --help] <searchPath>

Arguments:
    <searchPath>  Path to search. Defaults to current directory.

Options:
    -p, --pattern  <searchPattern>
    --hidden
    -h, --help

""";
        Assert.Equal(text.NormalizeLineEndings(), help.NormalizeLineEndings());
    }

    [Fact]
    public void HelpAndBadOption()
    {
        string[] args = [ "-h", "--bad-option" ];
        var testConsole = new TestConsole();
        Assert.False(CmdLine.TryParse<FileSizeCommand>(args, testConsole, out _));
        var text = """
error: Unexpected argument: '--bad-option'
usage: FileSizeCommand [-p | --pattern <searchPattern>] [--hidden] [-h | --help]
<searchPath>

Arguments:
    <searchPath>  Path to search. Defaults to current directory.

Options:
    -p, --pattern  <searchPattern>
    --hidden
    -h, --help


""";
        Assert.Equal(text.NormalizeLineEndings(), testConsole.Output);
    }

    [Fact]
    public void BadOption()
    {
        string[] args = [ "--bad-option" ];
        var testConsole = new TestConsole();
        var ex = Assert.Throws<ArgumentSyntaxException>(() => CmdLine.ParseRaw<FileSizeCommand>(args));
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

        [CommandOption("-h|--help")]
        public bool? Help { get; init; }
    }

    [Fact]
    public void BasicCommandTest()
    {
        string[] cmdLine = [ "-f", "abc" ];
        var cmd = CmdLine.ParseRawWithHelp<BasicCommand>(cmdLine).Unwrap();
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
