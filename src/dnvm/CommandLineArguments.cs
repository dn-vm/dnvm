
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using Semver;
using Serde;
using Serde.CmdLine;
using Spectre.Console;
using StaticCs;

namespace Dnvm;

[GenerateDeserialize]
[Command("dnvm", Summary = "Install and manage .NET SDKs.")]
public partial record DnvmCommand
{
    [CommandOption("--enable-dnvm-previews", Description = "Enable dnvm previews.")]
    public bool? EnableDnvmPreviews { get; init; }

    [CommandGroup("command")]
    public DnvmSubCommand? Command { get; init; }
}

[Closed]
[GenerateDeserialize]
public abstract partial record DnvmSubCommand
{
    private DnvmSubCommand() { }

    [Command("install", Summary = "Install an SDK.")]
    public sealed partial record InstallCommand : DnvmSubCommand
    {
        [CommandParameter(0, "version", Description = "The version of the SDK to install.")]
        [SerdeMemberOptions(DeserializeProxy = typeof(SemVersionProxy))]
        public required SemVersion SdkVersion { get; init; }

        [CommandOption("-f|--force", Description = "Force install the given SDK, even if already installed")]
        public bool? Force { get; init; } = null;

        [CommandOption("-s|--sdk-dir", Description = "Install the SDK into a separate directory with the given name.")]
        [SerdeMemberOptions(DeserializeProxy = typeof(NullableRefProxy.De<SdkDirName, SdkDirNameProxy>))] // Treat as string
        public SdkDirName? SdkDir { get; init; } = null;

        [CommandOption("-v|--verbose", Description = "Print debugging messages to the console.")]
        public bool? Verbose { get; init; } = null;
    }

    [Command("track", Summary = "Start tracking a new channel.")]
    public sealed partial record TrackCommand : DnvmSubCommand
    {
        [CommandParameter(0, "channel", Description = "Track the channel specified.")]
        [SerdeMemberOptions(DeserializeProxy = typeof(CaseInsensitiveChannel))]
        public required Channel Channel { get; init; }

        /// <summary>
        /// URL to the dotnet feed containing the releases index and SDKs.
        /// </summary>
        [CommandOption("--feed-url", Description = "Set the feed URL to download the SDK from.")]
        public string? FeedUrl { get; init; }

        [CommandOption("-v|--verbose", Description = "Print debugging messages to the console.")]
        public bool? Verbose { get; init; } = null;

        [CommandOption("-f|--force",  Description = "Force tracking the given channel, even if already tracked.")]
        public bool? Force { get; init; } = null;

        /// <summary>
        /// Answer yes to every question or use the defaults.
        /// </summary>
        [CommandOption("-y", Description = "Answer yes to all prompts.")]
        public bool? Yes { get; init; } = null;

        [CommandOption("--prereqs", Description = "Print prereqs for dotnet on Ubuntu.")]
        public bool? Prereqs { get; init; } = null;

        /// <summary>
        /// When specified, install the SDK into a separate directory with the given name,
        /// translated to lower-case. Preview releases are installed into a directory named 'preview'
        /// by default.
        /// </summary>
        [CommandOption("-s|--sdk-dir", Description = "Track the channel in a separate directory with the given name.")]
        public string? SdkDir { get; init; } = null;
    }

    [Command("selfinstall", Summary = "Install dnvm to the local machine.")]
    public sealed partial record SelfInstallCommand : DnvmSubCommand
    {
        [CommandOption("-v|--verbose", Description = "Print debugging messages to the console.")]
        public bool? Verbose { get; init; } = null;

        [CommandOption("-f|--force", Description = "Force install the given SDK, even if already installed")]
        public bool? Force { get; init; } = null;

        [CommandOption("--feed-url", Description = "Set the feed URL to download the SDK from.")]
        public string? FeedUrl { get; init; }

        [CommandOption("-y", Description = "Answer yes to all prompts.")]
        public bool? Yes { get; init; } = null;

        [CommandOption("--update", Description = "[internal] Update the current dnvm installation. Only intended to be called from dnvm.")]
        public bool? Update { get; init; } = null;

        [CommandOption("--dest-path", Description = "Set the destination path for the dnvm executable.")]
        public string? DestPath { get; init; } = null;
    }

    [Command("update", Summary = "Update the installed SDKs or dnvm itself.")]
    public sealed partial record UpdateCommand : DnvmSubCommand
    {
        [CommandOption("--dnvm-url", Description = "Set the URL for the dnvm releases endpoint.")]
        public string? DnvmReleasesUrl { get; init; } = null;

        [CommandOption("--feed-url", Description = "Set the feed URL to download the SDK from.")]
        public string? FeedUrl { get; init; } = null;

        [CommandOption("-v|--verbose", Description = "Print debugging messages to the console.")]
        public bool? Verbose { get; init; } = null;

        [CommandOption("--self", Description = "Update dnvm itself in the current location.")]
        public bool? Self { get; init; } = null;

        [CommandOption("-y", Description = "Answer yes to all prompts.")]
        public bool? Yes { get; init; } = null;
    }

    [Command("list", Summary = "List installed SDKs.")]
    public sealed partial record ListCommand : DnvmSubCommand
    {
    }

    [Command("select", Summary = "Select the active SDK directory.", Description =
"Select the active SDK directory, meaning the directory that will be used when running `dotnet` " +
"commands. This is the same directory passed to the `-s` option for `dnvm install`.\n" +
"\n" +
"Note: This command does not change between SDK versions installed in the same directory. For " +
"that, use the built-in dotnet global.json file. Information about global.json can be found at " +
"https://learn.microsoft.com/en-us/dotnet/core/tools/global-json.")]
    public sealed partial record SelectCommand : DnvmSubCommand
    {
        [CommandParameter(0, "sdkDirName", Description = "The name of the SDK directory to select.")]
        public required string SdkDirName { get; init; }
    }

    [Command("untrack", Summary = "Remove a channel from the list of tracked channels.")]
    public sealed partial record UntrackCommand : DnvmSubCommand
    {
        [CommandParameter(0, "channel", Description = "The channel to untrack.")]
        [SerdeMemberOptions(DeserializeProxy = typeof(CaseInsensitiveChannel))]
        public required Channel Channel { get; init; }
    }

    [Command("uninstall", Summary = "Uninstall an SDK.")]
    public sealed partial record UninstallCommand : DnvmSubCommand
    {
        [CommandParameter(0, "sdkVersion", Description = "The version of the SDK to uninstall.")]
        [SerdeMemberOptions(DeserializeProxy = typeof(SemVersionProxy))]
        public required SemVersion SdkVersion { get; init; }

        [CommandOption("-s|--sdk-dir", Description = "Uninstall the SDK from the given directory.")]
        [SerdeMemberOptions(DeserializeProxy = typeof(NullableRefProxy.De<SdkDirName, SdkDirNameProxy>))] // Treat as string
        public SdkDirName? SdkDir { get; init; } = null;
    }

    [Command("prune", Summary = "Remove all SDKs with older patch versions.")]
    public sealed partial record PruneCommand : DnvmSubCommand
    {
        [CommandOption("-v|--verbose", Description = "Print extra debugging info to the console.")]
        public bool? Verbose { get; init; } = null;

        [CommandOption("--dry-run", Description = "Print the list of the SDKs to be uninstalled, but don't uninstall.")]
        public bool? DryRun { get; init; } = null;
    }

    [Command("restore", Summary = "Restore the SDK listed in the global.json file.",
        Description = "Downloads the SDK in the global.json in or above the current directory to the .dotnet folder in the same directory.")]
    public sealed partial record RestoreCommand : DnvmSubCommand
    {
    }

    /// <summary>
    /// Deserialize a named channel case-insensitively. Produces a user-friendly error message if the
    /// channel is not recognized.
    /// </summary>
    private sealed class CaseInsensitiveChannel : IDeserializeProvider<Channel>, IDeserialize<Channel>
    {
        public ISerdeInfo SerdeInfo => StringProxy.SerdeInfo;
        static IDeserialize<Channel> IDeserializeProvider<Channel>.Instance { get; } = new CaseInsensitiveChannel();
        private CaseInsensitiveChannel() { }

        public Channel Deserialize(IDeserializer deserializer)
        {
            try
            {
                return Channel.FromString(StringProxy.Instance.Deserialize(deserializer).ToLowerInvariant());
            }
            catch (DeserializeException)
            {
                var sep = Environment.NewLine + "\t- ";
                IEnumerable<Channel> channels = [new Channel.Latest(), new Channel.Preview(), new Channel.Lts(), new Channel.Sts()];
                throw new DeserializeException(
                    "Channel must be one of:"
                    + sep + string.Join(sep, channels));
            }
        }
    }
}

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
        /// <summary>
        /// Path to overwrite.
        /// </summary>
        public string? DestPath { get; init; } = null;
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

    public sealed record RestoreArguments : CommandArguments;
}

public sealed record class CommandLineArguments(CommandArguments? Command)
{
    public bool EnableDnvmPreviews { get; init; } = false;

    public static CommandLineArguments? TryParse(IAnsiConsole console, string[] args)
    {
        try
        {
            return ParseRaw(console, args);
        }
        catch (ArgumentSyntaxException ex)
        {
            console.WriteLine("error: " + ex.Message);
            console.WriteLine(CmdLine.GetHelpText(SerdeInfoProvider.GetDeserializeInfo<DnvmCommand>(), includeHelp: true));
            return null;
        }
    }

    /// <summary>
    /// Throws an exception if the command was not recognized.
    /// </summary>
    public static CommandLineArguments ParseRaw(IAnsiConsole console, string[] args)
    {
        var result = CmdLine.ParseRaw<DnvmCommand>(args, handleHelp: true);
        DnvmCommand dnvmCmd;
        switch (result)
        {
            case Result<DnvmCommand, IReadOnlyList<ISerdeInfo>>.Ok(var value):
                dnvmCmd = value;
                break;
            case Result<DnvmCommand, IReadOnlyList<ISerdeInfo>>.Err(var helpInfos):
                var rootInfo = SerdeInfoProvider.GetDeserializeInfo<DnvmCommand>();
                var lastInfo = helpInfos.Last();
                console.WriteLine(CmdLine.GetHelpText(rootInfo, lastInfo, includeHelp: true));
                return new CommandLineArguments(Command: null);
            default:
                throw new InvalidOperationException();
        }
        if (dnvmCmd.EnableDnvmPreviews == true)
        {
            return new CommandLineArguments(Command: null) {
                EnableDnvmPreviews = true
            };
        }
        if (dnvmCmd.EnableDnvmPreviews == true)
        {
            return new CommandLineArguments(Command: null) {
                EnableDnvmPreviews = true
            };
        }
        switch (dnvmCmd.Command)
        {
            case DnvmSubCommand.ListCommand c:
            {
                return new CommandLineArguments(new CommandArguments.ListArguments());
            }
            case DnvmSubCommand.SelectCommand s:
            {
                return new CommandLineArguments(new CommandArguments.SelectArguments { SdkDirName = s.SdkDirName });
            }
            case DnvmSubCommand.UntrackCommand u:
            {
                return new CommandLineArguments(new CommandArguments.UntrackArguments { Channel = u.Channel });
            }
            case DnvmSubCommand.InstallCommand i:
            {
                return new CommandLineArguments(new CommandArguments.InstallArguments
                {
                    SdkVersion = i.SdkVersion,
                    Force = i.Force ?? false,
                    SdkDir = i.SdkDir,
                    Verbose = i.Verbose ?? false,
                });
            }
            case DnvmSubCommand.TrackCommand t:
            {
                return new CommandLineArguments(new CommandArguments.TrackArguments
                {
                    Channel = t.Channel,
                    Verbose = t.Verbose ?? false,
                    Force = t.Force ?? false,
                    Yes = t.Yes ?? false,
                    Prereqs = t.Prereqs ?? false,
                    FeedUrl = t.FeedUrl,
                    SdkDir = t.SdkDir,
                });
            }
            case DnvmSubCommand.SelfInstallCommand s:
            {
                return new CommandLineArguments(new CommandArguments.SelfInstallArguments
                {
                    Verbose = s.Verbose ?? false,
                    Yes = s.Yes ?? false,
                    FeedUrl = s.FeedUrl,
                    Force = s.Force ?? false,
                    Update = s.Update ?? false,
                    DestPath = s.DestPath,
                });
            }
            case DnvmSubCommand.UpdateCommand u:
            {
                return new CommandLineArguments(new CommandArguments.UpdateArguments
                {
                    Self = u.Self ?? false,
                    Verbose = u.Verbose ?? false,
                    FeedUrl = u.FeedUrl,
                    DnvmReleasesUrl = u.DnvmReleasesUrl,
                });
            }
            case DnvmSubCommand.UninstallCommand u:
            {
                return new CommandLineArguments(new CommandArguments.UninstallArguments
                {
                    SdkVersion = u.SdkVersion,
                    Dir = u.SdkDir,
                });
            }
            case DnvmSubCommand.PruneCommand p:
            {
                return new CommandLineArguments(new CommandArguments.PruneArguments
                {
                    Verbose = p.Verbose ?? false,
                    DryRun = p.DryRun ?? false,
                });
            }
            case DnvmSubCommand.RestoreCommand r:
            {
                return new CommandLineArguments(new CommandArguments.RestoreArguments());
            }
            case null:
            {
                console.WriteLine(CmdLine.GetHelpText(SerdeInfoProvider.GetDeserializeInfo<DnvmCommand>(), includeHelp: true));
                return new CommandLineArguments(Command: null);
            }
        }

        throw new DeserializeException("Unknown command");
    }
}