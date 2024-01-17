
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using Internal.CommandLine;
using Semver;
using Serde;
using StaticCs;

namespace Dnvm;

[Closed]
public abstract record CommandArguments
{
    private CommandArguments() {}

    public sealed record InstallArguments : CommandArguments
    {
        public required SemVersion SdkVersion { get; init; }
        public bool Force { get; init; } = false;
        public SdkDirName? SdkDir { get; init; } = null;
        public bool Verbose { get; init; } = false;
    }

    public sealed record TrackArguments : CommandArguments
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

    public sealed record UntrackArguments : CommandArguments
    {
        public required Channel Channel { get; init; }
    }

    public sealed record UninstallArguments : CommandArguments
    {
        public required SemVersion SdkVersion { get; init; }
        public SdkDirName? Dir { get; init; } = null;
    }

    public sealed record PruneArguments : CommandArguments
    {
        public bool Verbose { get; init; } = false;
        public bool DryRun { get; init; } = false;
    }
}

public sealed record class CommandLineArguments(CommandArguments Command)
{
    public static CommandLineArguments Parse(string[] args) => Parse(true, args);

    public static CommandLineArguments Parse(bool handleErrors, string[] args)
    {
        CommandArguments? command = default;

        var argSyntax = ArgumentSyntax.Parse(args, syntax =>
        {
            syntax.HandleErrors = handleErrors;

            string? commandName = null;

            var track = syntax.DefineCommand("track", ref commandName, "Start tracking a new channel");
            if (track.IsActive)
            {
                Channel channel = new Channel.Latest();
                bool verbose = default;
                bool force = default;
                bool yes = false;
                bool prereqs = default;
                string? feedUrl = default;
                string? sdkDir = null;
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineOption("f|force", ref force, "Force tracking the given channel, even if already tracked.");
                syntax.DefineOption("y", ref yes, "Answer yes to every question (or accept default).");
                syntax.DefineOption("prereqs", ref prereqs, "Print prereqs for dotnet on Ubuntu");
                syntax.DefineOption("feed-url", ref feedUrl, $"Set the feed URL to download the SDK from.");
                syntax.DefineOption("s|sdkDir", ref sdkDir, "Track the channel in a separate directory with the given name.");
                syntax.DefineParameter("channel", ref channel, ChannelParse,
                    $"Track the channel specified. Defaults to '{channel.ToString().ToLowerInvariant()}'.");

                command = new CommandArguments.TrackArguments
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

            var install = syntax.DefineCommand("install", ref commandName, "Install an SDK");
            if (install.IsActive)
            {
                SemVersion version = default!;
                bool force = false;
                SdkDirName? sdkDir = null;
                bool verbose = default!;

                syntax.DefineOption("f|force", ref force, "Force install the given SDK, even if already installed");
                syntax.DefineOption("s|sdk-dir", ref sdkDir, s => new SdkDirName(s), "Install the SDK into a separate directory with the given name.");
                syntax.DefineOption("v|verbose", ref verbose, "Print debugging messages to the console.");
                syntax.DefineParameter("version", ref version!, v =>
                {
                    if (SemVersion.TryParse(v, SemVersionStyles.Strict, out var result))
                    {
                        return result;
                    }
                    throw new FormatException($"Invalid version: {v}");
                }, "The version of the SDK to install.");

                command = new CommandArguments.InstallArguments
                {
                    SdkVersion = version,
                    Force = force,
                    SdkDir = sdkDir,
                    Verbose = verbose
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

            var untrack = syntax.DefineCommand("untrack", ref commandName, "Remove a channel from the list of tracked channels");
            if (untrack.IsActive)
            {
                Channel? channel = null;
                syntax.DefineParameter("channel", ref channel, ChannelParse, $"Remove the given channel from the list of tracked channels.");

                command = new CommandArguments.UntrackArguments
                {
                    Channel = channel!,
                };
            }

            var uninstall = syntax.DefineCommand("uninstall", ref commandName, "Uninstall an SDK");
            if (uninstall.IsActive)
            {
                SemVersion? sdkVersion = null;
                syntax.DefineParameter<SemVersion>("sdkVersion", ref sdkVersion!, v =>
                {
                    if (SemVersion.TryParse(v, SemVersionStyles.Strict, out var result))
                    {
                        return result;
                    }
                    throw new FormatException($"Invalid version: {v}");
                }, "The version of the SDK to uninstall.");

                command = new CommandArguments.UninstallArguments
                {
                    SdkVersion = sdkVersion!,
                };
            }

            var prune = syntax.DefineCommand("prune", ref commandName, "Remove all SDKs with older patch versions.");
            if (prune.IsActive)
            {
                bool dryRun = false;
                bool verbose = false;
                syntax.DefineOption("dry-run", ref dryRun, "Print the list of the SDKs to be uninstalled, but don't uninstall.");
                syntax.DefineOption("v|verbose", ref verbose, "Print extra debugging info to the console");
                command = new CommandArguments.PruneArguments
                {
                    DryRun = dryRun,
                    Verbose = verbose
                };
            }
        });

        if (command is null)
        {
            throw new InvalidOperationException("Expected command or exception");
        }

        return new CommandLineArguments(command);
    }

    private static Channel ChannelParse(string channel)
    {
        var scalarDeserializer = new ScalarDeserializer(channel);
        try
        {
            var result = Channel.Deserialize(ref scalarDeserializer);
            return result;
        }
        catch (InvalidDeserializeValueException)
        {
            var sep = Environment.NewLine + "\t- ";
            IEnumerable<Channel> channels = [new Channel.Latest(), new Channel.Preview(), new Channel.Lts(), new Channel.Sts()];
            throw new FormatException(
                "Channel must be one of:"
                + sep + string.Join(sep, channels));
        }
    }
}