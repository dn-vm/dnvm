
using System;
using Internal.CommandLine;

namespace Dnvm;

public enum Channel
{
    LTS,
    Current,
    Preview
}

public abstract record Command
{
    private Command() {}
    public sealed record InstallOptions : Command
    {
        public bool Verbose { get; init; } = false;
        public Channel Channel { get; init; } = Channel.LTS;
        public bool Force { get; init; } = false;
        public bool Self { get; init; } = false;
        public bool Prereqs { get; init; } = false;
        public bool Global { get; init; } = false;
        /// <summary>
        /// Set the URL to the dotnet feed to install from.
        /// </summary>
        public string? FeedUrl { get; init; } = null;
        /// <summary>
        /// Path to install to.
        /// </summary>
        public string? InstallPath { get; init; } = null;
    }

    public sealed record UpdateOptions : Command
    {
        public bool Verbose { get; init; } = false;
        public bool Self { get; init; } = false;
    }
}

sealed record class CommandLineOptions(Command Command)
{
    public static CommandLineOptions Parse(string[] args)
    {
        Command? command = default;

        var argSyntax = ArgumentSyntax.Parse(args, syntax =>
        {
            string? commandName = null;

            var install = syntax.DefineCommand("install", ref commandName, "Install a new SDK");
            if (install.IsActive)
            {
                Channel channel = default;
                bool verbose = default;
                bool force = default;
                bool self = default;
                bool prereqs = default;
                bool global = default;
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption(
                    "c|channel",
                    ref channel,
                    c => c.ToLower() switch
                    {
                        "lts" => Channel.LTS,
                        "current" => Channel.Current,
                        "preview" => Channel.Preview,
                        _ => throw new FormatException("Channel must be one of 'lts' or 'current'")
                    },
                    $"Download from the channel specified, Defaults to ${channel}.");
                syntax.DefineOption("f|force", ref force, "Force install the given SDK, even if already installed");
                syntax.DefineOption("self", ref self, "Install dnvm itself into the target location");
                syntax.DefineOption("prereqs", ref prereqs, "Print prereqs for dotnet on Ubuntu");
                syntax.DefineOption("g|global", ref global, "Install to the global location");
                command = new Command.InstallOptions
                {
                    Channel = channel,
                    Verbose = verbose,
                    Force = force,
                    Self = self,
                    Prereqs = prereqs,
                    Global = global
                };
            }

            var update = syntax.DefineCommand("update", ref commandName, "Update the installed SDKs or dnvm itself");
            if (update.IsActive)
            {
                bool self = default;
                bool verbose = default;
                syntax.DefineOption("self", ref self, "Update dnvm itself in the current location");
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");

                command = new Command.UpdateOptions
                {
                    Self = self,
                    Verbose = verbose
                };
            }
        });

        return new CommandLineOptions(command!);
    }
}