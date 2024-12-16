
using System.IO;
using System.Linq;
using Spectre.Console.Testing;
using Xunit;

namespace Serde.CmdLine;

public sealed partial class SubCommandTests
{
    [Fact]
    public void NoSubCommand()
    {
        string[] testArgs = [ "-v" ];
        var cmd = CmdLine.ParseRaw<TopCommand>(testArgs);
        Assert.Equal(new TopCommand { Verbose = true, SubCommand = null }, cmd);
    }

    [Fact]
    public void FirstCommand()
    {
        string[] testArgs = [ "-v", "first" ];
        var cmd = CmdLine.ParseRaw<TopCommand>(testArgs);
        Assert.Equal(new TopCommand { Verbose = true, SubCommand = new SubCommand.FirstCommand() }, cmd);
    }

    [Fact]
    public void TopLevelHelp()
    {
        var help = CmdLine.GetHelpText(SerdeInfoProvider.GetInfo<TopCommand>());
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

        [Command("command")]
        public SubCommand? SubCommand { get; init; }
    }

    private partial record SubCommand : IDeserializeProvider<SubCommand>
    {
        static IDeserialize<SubCommand> IDeserializeProvider<SubCommand>.DeserializeInstance => Deserialize.Instance;
        private SubCommand() { }

        public static ISerdeInfo SerdeInfo { get; } = new UnionSerdeInfo(
            typeof(SubCommand).ToString(),
            typeof(SubCommand).GetCustomAttributesData(),
            [
                SerdeInfoProvider.GetInfo<FirstCommandProxy>(),
                SerdeInfoProvider.GetInfo<SecondCommandProxy>()
            ]);

        private sealed class Deserialize : IDeserialize<SubCommand>
        {
            public static readonly Deserialize Instance = new Deserialize();
            private Deserialize() { }

            SubCommand IDeserialize<SubCommand>.Deserialize(IDeserializer deserializer)
            {
                var subCmd = StringProxy.Instance.Deserialize(deserializer);
                return subCmd switch
                {
                    "first" => new FirstCommand(),
                    "second" => new SecondCommand(),
                    _ => throw new ArgumentSyntaxException($"Unknown subcommand '{subCmd}'.")
                };
            }
        }

        // Use proxies for deserialization as we want to avoid exposing deserialization for the
        // union types themselves. We want all deserialization to go through the parent type.
        [GenerateDeserialize(ForType = typeof(FirstCommand))]
        private sealed partial record FirstCommandProxy;
        [GenerateDeserialize(ForType = typeof(SecondCommand))]
        private sealed partial record SecondCommandProxy;

        [Command("first")]
        public sealed partial record FirstCommand : SubCommand;
        [Command("second")]
        public sealed partial record SecondCommand : SubCommand;
    }
}