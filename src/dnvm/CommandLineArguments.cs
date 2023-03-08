
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
        /// URL to the dotnet feed containing the releases index and SDKs.
        /// </summary>
        public string? FeedUrl { get; init; }
        public bool Verbose { get; init; } = false;
        public bool Force { get; init; } = false;
        /// <summary>
        /// Answer yes to every question or use the defaults.
        /// </summary>
        public bool Yes { get; init; } = false;
        public bool Prereqs { get; init; } = false;
        /// <summary>
        /// When specified, install the SDK into a separate directory with the given name,
        /// translated to lower-case. Preview releases are installed into a directory named 'preview'
        /// by default.
        /// </summary>
        public string? SdkDir { get; init; } = null;
    }

    public sealed record SelfInstallArguments : CommandArguments
    {
        public bool Verbose { get; init; } = false;
        public bool Force { get; init; } = false;
        /// <summary>
        /// URL to the dotnet feed containing the releases index and download artifacts.
        /// </summary>
        public string? FeedUrl { get; init; }
        /// <summary>
        /// Answer yes to every question or use the defaults.
        /// </summary>
        public bool Yes { get; init; } = false;
        /// <summary>
        /// When true, add dnvm update lines to the user's config files
        /// or environment variables.
        /// </summary>
        public bool UpdateUserEnvironment { get; init; } = true;
        /// <summary>
        /// Indicates that this is an update to an existing dnvm installation.
        /// </summary>
        public bool Update { get; init; } = false;
    }

    public sealed record UpdateArguments : CommandArguments
    {
        /// <summary>
        /// URL to the dnvm releases.json file listing the latest releases and their download
        /// locations.
        /// </summary>
        public string? DnvmReleasesUrl { get; init; }
        public string? FeedUrl { get; init; }
        public bool Verbose { get; init; } = false;
        public bool Self { get; init; } = false;
        /// <summary>
        /// Implicitly answers 'yes' to every question.
        /// </summary>
        public bool Yes { get; init; } = false;
    }

    public sealed record ListArguments : CommandArguments
    { }

    public sealed record SelectArguments : CommandArguments
    {
        public required string SdkDirName { get; init; }
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
                bool yes = false;
                bool prereqs = default;
                string? feedUrl = default;
                string? sdkDir = null;
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("f|force", ref force, "Force install the given SDK, even if already installed");
                syntax.DefineOption("y", ref yes, "Answer yes to every question (or accept default).");
                syntax.DefineOption("prereqs", ref prereqs, "Print prereqs for dotnet on Ubuntu");
                syntax.DefineOption("feed-url", ref feedUrl, $"Set the feed URL to download the SDK from.");
                syntax.DefineOption("s|sdkDir", ref sdkDir, "Install the SDK into a separate directory with the given name.");
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
                    Yes = yes,
                    Prereqs = prereqs,
                    FeedUrl = feedUrl,
                    SdkDir = sdkDir,
                };
            }

            var selfInstall = syntax.DefineCommand("selfinstall", ref commandName, "Install dnvm to the local machine");
            if (selfInstall.IsActive)
            {
                bool verbose = default;
                bool force = default;
                bool yes = false;
                string? feedUrl = default;
                bool selfUpdate = false;

                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("f|force", ref force, "Force install the given SDK, even if already installed");
                syntax.DefineOption("y", ref yes, "Answer yes to every question (or accept default).");
                syntax.DefineOption("feed-url", ref feedUrl, $"Set the feed URL to download the SDK from.");
                syntax.DefineOption("update", ref selfUpdate, "[internal] Update the dnvm installation in the current location. Only intended to be called from dnvm.");

                command = new CommandArguments.SelfInstallArguments
                {
                    Verbose = verbose,
                    Yes = yes,
                    FeedUrl = feedUrl,
                    Force = force,
                    Update = selfUpdate,
                };
            }

            var update = syntax.DefineCommand("update", ref commandName, "Update the installed SDKs or dnvm itself");
            if (update.IsActive)
            {
                bool self = default;
                bool verbose = default;
                string? feedUrl = default;
                string? dnvmReleasesUrl = null;
                syntax.DefineOption("self", ref self, "Update dnvm itself in the current location");
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("dnvm-url", ref dnvmReleasesUrl, $"Set the URL for the dnvm releases endpoint.");
                syntax.DefineOption("feed-url", ref feedUrl, $"Set the feed URL to download the SDK from. Default is {feedUrl}");

                command = new CommandArguments.UpdateArguments
                {
                    Self = self,
                    Verbose = verbose,
                    FeedUrl = feedUrl,
                    DnvmReleasesUrl = dnvmReleasesUrl,
                };
            }

            var list = syntax.DefineCommand("list", ref commandName, "List installed SDKs");
            if (list.IsActive)
            {
                command = new CommandArguments.ListArguments();
            }

            var select = syntax.DefineCommand("select", ref commandName, "Select the active SDK directory");
            if (select.IsActive)
            {
                string? sdkDirName = null;
                syntax.DefineParameter("sdkDirName", ref sdkDirName!, "The name of the SDK directory to select.");
                if (sdkDirName is null)
                {
                    throw new FormatException("The select command requires a parameter specifying the SDK directory to select.");
                }
                command = new CommandArguments.SelectArguments
                {
                    SdkDirName = sdkDirName,
                };
            }
        });

        if (command is null)
        {
            throw new InvalidOperationException("Expected command or exception");
        }

        return new CommandLineArguments(command);
    }

}