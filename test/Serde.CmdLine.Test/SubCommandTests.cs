
using System.IO;
using System.Linq;
using Spectre.Console.Testing;
using Xunit;

namespace Serde.CmdLine.Test;

public sealed partial class SubCommandTests
{
    [Fact]
    public void NoSubCommand()
    {
        string[] testArgs = [ "-v" ];
        var cmd = CmdLine.ParseRawWithHelp<TopCommand>(testArgs).Unwrap();
        Assert.Equal(new TopCommand { Verbose = true, SubCommand = null }, cmd);
    }

    [Fact]
    public void FirstCommand()
    {
        string[] testArgs = [ "-v", "first" ];
        var cmd = CmdLine.ParseRawWithHelp<TopCommand>(testArgs).Unwrap();
        Assert.Equal(new TopCommand { Verbose = true, SubCommand = new SubCommand.FirstCommand() }, cmd);
    }

    [Fact]
    public void TopLevelHelp()
    {
        var help = CmdLine.GetHelpText(SerdeInfoProvider.GetDeserializeInfo<TopCommand>());
        var text = """
usage: TopCommand [-v | --verbose] [-h | --help] <command>

Options:
    -v, --verbose
    -h, --help

Commands:
    first
    second

""";
        Assert.Equal(text.NormalizeLineEndings(), help.NormalizeLineEndings());
    }

    [GenerateDeserialize]
    private partial record TopCommand
    {
        [CommandOption("-v|--verbose")]
        public bool? Verbose { get; init; }

        [CommandOption("-h|--help")]
        public bool? Help { get; init; }

        [CommandGroup("command")]
        public SubCommand? SubCommand { get; init; }
    }

    [GenerateDeserialize]
    private abstract partial record SubCommand
    {
        private SubCommand() { }

        [Command("first")]
        public sealed partial record FirstCommand : SubCommand;
        [Command("second")]
        public sealed partial record SecondCommand : SubCommand;
    }
}