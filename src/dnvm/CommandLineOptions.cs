
using System;
using System.Diagnostics;
using Internal.CommandLine;

namespace Dnvm;

public abstract record CommandArguments
{
    private CommandArguments() {}
    public sealed record InstallArguments : CommandArguments
    {
        public required Channel Channel { get; init; }
        /// <summary>
        /// URL to the dotnet feed containing the releases index and download artifacts.
        /// </summary>
        public string? FeedUrl { get; init; }
        public bool Verbose { get; init; } = false;
        public bool Force { get; init; } = false;
        public bool Self { get; init; } = false;
        public bool Prereqs { get; init; } = false;
        /// <summary>
        /// Directory to place the dnvm exe.
        /// </summary>
        public string? DnvmInstallPath { get; init; } = null;
        /// <summary>
        /// When true, add dnvm update lines to the user's config files
        /// or environment variables.
        /// </summary>
        public bool UpdateUserEnvironment { get; init; } = true;
    }

    public sealed record UpdateArguments : CommandArguments
    {
        public string? FeedUrl { get; init; }
        public bool Verbose { get; init; } = false;
        public bool Self { get; init; } = false;
        /// <summary>
        /// Implicitly answers 'yes' to every question.
        /// </summary>
        public bool Yes { get; init; } = false;
    }
}

sealed record class CommandLineArguments(CommandArguments Command)
{
    public static CommandLineArguments Parse(string[] args)
    {
        CommandArguments? command = default;

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
                string? feedUrl = default;
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("f|force", ref force, "Force install the given SDK, even if already installed");
                syntax.DefineOption("self", ref self, "Install dnvm itself into the target location");
                syntax.DefineOption("prereqs", ref prereqs, "Print prereqs for dotnet on Ubuntu");
                syntax.DefineOption("feed-url", ref feedUrl, $"Set the feed URL to download the SDK from.");
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
                command = new CommandArguments.InstallArguments
                {
                    Channel = channel,
                    Verbose = verbose,
                    Force = force,
                    Self = self,
                    Prereqs = prereqs,
                    FeedUrl = feedUrl,
                };
            }

            var update = syntax.DefineCommand("update", ref commandName, "Update the installed SDKs or dnvm itself");
            if (update.IsActive)
            {
                bool self = default;
                bool verbose = default;
                string? feedUrl = default;
                syntax.DefineOption("self", ref self, "Update dnvm itself in the current location");
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("feed-url", ref feedUrl, $"Set the feed URL to download the SDK from. Default is {feedUrl}");

                command = new CommandArguments.UpdateArguments
                {
                    Self = self,
                    Verbose = verbose,
                    FeedUrl = feedUrl
                };
            }
        });

        return new CommandLineArguments(command!);
    }
}