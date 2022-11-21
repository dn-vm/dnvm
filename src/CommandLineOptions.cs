
using System;
using Internal.CommandLine;

namespace Dnvm;

public abstract record Command
{
    private Command() {}
    public sealed record InstallOptions : Command
    {
        public required Channel Channel { get; init; }
        public bool Verbose { get; init; } = false;
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

        /// <summary>
        /// When true, add dnvm update lines to the user's config files
        /// or environment variables.
        /// </summary>
        public bool UpdateUserEnvironment { get; init; } = true;
    }

    public sealed record UpdateOptions : Command
    {
        public bool Verbose { get; init; } = false;
        public bool Self { get; init; } = false;
        public string? ReleasesUrl { get; init; } = null;
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
                Channel channel = Channel.Latest;
                bool verbose = default;
                bool force = default;
                bool self = default;
                bool prereqs = default;
                bool global = default;
                string? feedUrl = null;
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("f|force", ref force, "Force install the given SDK, even if already installed");
                syntax.DefineOption("g|global", ref global, "Install to the global location");
                syntax.DefineOption("self", ref self, "Install dnvm itself into the target location");
                syntax.DefineOption("prereqs", ref prereqs, "Print prereqs for dotnet on Ubuntu");
                syntax.DefineOption("feed-url", ref feedUrl, "Set the feed URL to download the SDK from.");
                syntax.DefineParameter("channel", ref channel, c =>
                {
                    if (Enum.TryParse<Channel>(c, ignoreCase: true, out var result))
                    {
                        return result;
                    }
                    var sep = Environment.NewLine + "\t- ";
                    throw new FormatException(
                        "Channel must be one of:"
                        + sep + string.Join(sep, Enum.GetNames<Channel>()));
                },
                $"Download from the channel specified. Defaults to '{channel.ToString().ToLowerInvariant()}'.");
                command = new Command.InstallOptions
                {
                    Channel = channel,
                    Verbose = verbose,
                    Force = force,
                    Self = self,
                    Prereqs = prereqs,
                    Global = global,
                    FeedUrl = feedUrl,
                };
            }

            var update = syntax.DefineCommand("update", ref commandName, "Update the installed SDKs or dnvm itself");
            if (update.IsActive)
            {
                bool self = default;
                bool verbose = default;
                string? releasesUrl = default;
                syntax.DefineOption("self", ref self, "Update dnvm itself in the current location");
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("releases-url", ref releasesUrl, "Url to fetch info for location of latest releases.");

                command = new Command.UpdateOptions
                {
                    Self = self,
                    Verbose = verbose,
                    ReleasesUrl = releasesUrl
                };
            }
        });

        return new CommandLineOptions(command!);
    }
}