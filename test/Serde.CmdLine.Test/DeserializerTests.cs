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
        var cmd = CmdLine.Parse<FileSizeCommand>(testArgs);
        Assert.Equal(new FileSizeCommand { SearchPath = null, SearchPattern = "*.txt", IncludeHidden = true }, cmd);
    }

    [Fact]
    public void TestSearchPath()
    {
        string[] testArgs = [ "search-path" ];
        var cmd = CmdLine.Parse<FileSizeCommand>(testArgs);
        Assert.Equal(new FileSizeCommand { SearchPath = "search-path", SearchPattern = null, IncludeHidden = null }, cmd);
    }

    [Fact]
    public void TestHelp()
    {
        string[] args = [ "--help" ];
        var testConsole = new TestConsole();
        CmdLine.Run<FileSizeCommand>(args, testConsole, fileSizeCmd => { });
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
}
