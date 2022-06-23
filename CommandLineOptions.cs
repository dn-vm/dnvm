
using System;
using Internal.CommandLine;

namespace Dnvm;

public enum Channel
{
    LTS,
    Current,
    Preview
}

public record class CommandLineOptions
{
    public bool Verbose { get; init; } = false;
    public Channel Channel { get; init; } = Channel.LTS;
    public bool Force { get; init; } = false;

    public static CommandLineOptions Parse(string[] args)
    {
        Channel channel = default;
        bool verbose = default;
        bool force = default;

        var argSyntax = ArgumentSyntax.Parse(args, syntax =>
        {
            string installString = "";
            var installCommand = syntax.DefineCommand("install", ref installString, "Install a new SDK");
            syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
            syntax.DefineOption(
                "c|channel",
                ref channel,
                c => c.ToLower() switch {
                    "lts" => Channel.LTS,
                    "current" => Channel.Current,
                    "preview" => Channel.Preview,
                    _ => throw new FormatException("Channel must be one of 'lts' or 'current'")
                },
                $"Download from the channel specified, Defaults to ${channel}.");
            syntax.DefineOption("f|force", ref force, "Force install the given SDK, even if already installed");
        });

        return new CommandLineOptions()
        {
            Channel = channel,
            Verbose = verbose,
            Force = force
        };
    }
}