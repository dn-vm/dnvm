
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    [CommandOption("-h|--help", Description = "Show help information.")]
    public bool? Help { get; init; }

    [Command("command")]
    public DnvmSubCommand? Command { get; init; }
}

[Closed]
public abstract partial record DnvmSubCommand : IDeserialize<DnvmSubCommand>
{
    private DnvmSubCommand() { }

    static ISerdeInfo ISerdeInfoProvider.SerdeInfo { get; } = new UnionSerdeInfo(
        nameof(DnvmSubCommand),
        typeof(DnvmSubCommand).GetCustomAttributesData(),
        [
            SerdeInfoProvider.GetInfo<TrackCommandProxy>(),
            SerdeInfoProvider.GetInfo<InstallCommandProxy>(),
            SerdeInfoProvider.GetInfo<SelfInstallCommandProxy>(),
            SerdeInfoProvider.GetInfo<UpdateCommandProxy>(),
            SerdeInfoProvider.GetInfo<ListCommandProxy>(),
            SerdeInfoProvider.GetInfo<SelectCommandProxy>(),
            SerdeInfoProvider.GetInfo<UntrackCommandProxy>(),
            SerdeInfoProvider.GetInfo<UninstallCommandProxy>(),
            SerdeInfoProvider.GetInfo<PruneCommandProxy>()
        ]);

    [GenerateDeserialize(ThroughType = typeof(InstallCommand))]
    internal partial struct InstallCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(TrackCommand))]
    internal partial struct TrackCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(SelfInstallCommand))]
    internal partial struct SelfInstallCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(ListCommand))]
    internal partial struct ListCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(SelectCommand))]
    internal partial struct SelectCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(UntrackCommand))]
    internal partial struct UntrackCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(UpdateCommand))]
    internal partial struct UpdateCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(UninstallCommand))]
    internal partial struct UninstallCommandProxy;
    [GenerateDeserialize(ThroughType = typeof(PruneCommand))]
    internal partial struct PruneCommandProxy;

    static DnvmSubCommand IDeserialize<DnvmSubCommand>.Deserialize(IDeserializer deserializer)
    {
        var commandName = StringWrap.Deserialize(deserializer);
        DnvmSubCommand subCommand = commandName switch
        {
            "install" => DeserializeSubCommand<InstallCommand, InstallCommandProxy>(deserializer),
            "track" => DeserializeSubCommand<TrackCommand, TrackCommandProxy>(deserializer),
            "selfinstall" => DeserializeSubCommand<SelfInstallCommand, SelfInstallCommandProxy>(deserializer),
            "update" => DeserializeSubCommand<UpdateCommand, UpdateCommandProxy>(deserializer),
            "list" => DeserializeSubCommand<ListCommand, ListCommandProxy>(deserializer),
            "select" => DeserializeSubCommand<SelectCommand, SelectCommandProxy>(deserializer),
            "untrack" => DeserializeSubCommand<UntrackCommand, UntrackCommandProxy>(deserializer),
            "uninstall" => DeserializeSubCommand<UninstallCommand, UninstallCommandProxy>(deserializer),
            "prune" => DeserializeSubCommand<PruneCommand, PruneCommandProxy>(deserializer),
            _ => throw new InvalidDeserializeValueException($"Unknown command: {commandName}")
        };
        return subCommand;
    }

    private static T DeserializeSubCommand<T, TProxy>(IDeserializer deserializer)
        where T : DnvmSubCommand
        where TProxy : IDeserialize<T>
    {
        return TProxy.Deserialize(deserializer);
    }

    [Command("install", Summary = "Install an SDK.")]
    public sealed partial record InstallCommand : DnvmSubCommand
    {
        [CommandParameter(0, "version", Description = "The version of the SDK to install.")]
        [SerdeMemberOptions(WrapperDeserialize = typeof(NullableRefWrap.DeserializeImpl<SemVersion, SemVersionSerdeWrap>))]
        public SemVersion? SdkVersion { get; init; }

        [CommandOption("-f|--force", Description = "Force install the given SDK, even if already installed")]
        public bool? Force { get; init; } = null;

        [CommandOption("-s|--sdk-dir", Description = "Install the SDK into a separate directory with the given name.")]
        [SerdeMemberOptions(WrapperDeserialize = typeof(NullableWrap.DeserializeImpl<SdkDirName, SdkDirNameProxy>))] // Treat as string
        public SdkDirName? SdkDir { get; init; } = null;

        [CommandOption("-v|--verbose", Description = "Print debugging messages to the console.")]
        public bool? Verbose { get; init; } = null;

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; }
    }

    [Command("track", Summary = "Start tracking a new channel.")]
    public sealed partial record TrackCommand : DnvmSubCommand
    {
        [CommandParameter(0, "channel", Description = "Track the channel specified. Defaults to 'latest'.")]
        [SerdeMemberOptions(WrapperDeserialize = typeof(NullableRefWrap.DeserializeImpl<Channel, CaseInsensitiveChannel>))]
        public Channel? Channel { get; init; } = null;

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

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; } = null;
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

        [CommandOption("--update", Description = "[internal] Update the dnvm installation in the current location. Only intended to be called from dnvm.")]
        public bool? Update { get; init; } = null;

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; } = null;
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

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; } = null;
    }

    [Command("list", Summary = "List installed SDKs.")]
    public sealed partial record ListCommand : DnvmSubCommand
    {
        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; }
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
        public string? SdkDirName { get; init; }

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; }
    }

    [Command("untrack", Summary = "Remove a channel from the list of tracked channels.")]
    public sealed partial record UntrackCommand : DnvmSubCommand
    {
        [CommandParameter(0, "channel", Description = "The channel to untrack.")]
        [SerdeMemberOptions(WrapperDeserialize = typeof(NullableRefWrap.DeserializeImpl<Channel, CaseInsensitiveChannel>))]
        public Channel? Channel { get; init; }

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; }
    }

    [Command("uninstall", Summary = "Uninstall an SDK.")]
    public sealed partial record UninstallCommand : DnvmSubCommand
    {
        [CommandParameter(0, "sdkVersion", Description = "The version of the SDK to uninstall.")]
        [SerdeMemberOptions(WrapperDeserialize = typeof(NullableRefWrap.DeserializeImpl<SemVersion, SemVersionSerdeWrap>))]
        public SemVersion? SdkVersion { get; init; } = null;

        [CommandOption("-s|--sdk-dir", Description = "Uninstall the SDK from the given directory.")]
        [SerdeMemberOptions(WrapperDeserialize = typeof(NullableWrap.DeserializeImpl<SdkDirName, SdkDirNameProxy>))] // Treat as string
        public SdkDirName? SdkDir { get; init; } = null;

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; }
    }

    [Command("prune", Summary = "Remove all SDKs with older patch versions.")]
    public sealed partial record PruneCommand : DnvmSubCommand
    {
        [CommandOption("-v|--verbose", Description = "Print extra debugging info to the console.")]
        public bool? Verbose { get; init; } = null;

        [CommandOption("--dry-run", Description = "Print the list of the SDKs to be uninstalled, but don't uninstall.")]
        public bool? DryRun { get; init; } = null;

        [CommandOption("-h|--help", Description = "Show help information.")]
        public bool? Help { get; init; } = null;
    }

    /// <summary>
    /// Deserialize a named channel case-insensitively. Produces a user-friendly error message if the
    /// channel is not recognized.
    /// </summary>
    private readonly struct CaseInsensitiveChannel : IDeserialize<Channel>
    {
        public static ISerdeInfo SerdeInfo => Serde.SerdeInfo.MakePrimitive(nameof(Channel));

        public static Channel Deserialize(IDeserializer deserializer)
        {
            try
            {
                return Channel.FromString(StringWrap.Deserialize(deserializer).ToLowerInvariant());
            }
            catch (InvalidDeserializeValueException)
            {
                var sep = Environment.NewLine + "\t- ";
                IEnumerable<Channel> channels = [new Channel.Latest(), new Channel.Preview(), new Channel.Lts(), new Channel.Sts()];
                throw new InvalidDeserializeValueException(
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

public sealed record class CommandLineArguments(CommandArguments? Command)
{
    public static CommandLineArguments? TryParse(IAnsiConsole console, string[] args)
    {
        try
        {
            return ParseRaw(console, args);
        }
        catch (ArgumentSyntaxException ex)
        {
            console.WriteLine("error: " + ex.Message);
            console.WriteLine(CmdLine.GetHelpText(SerdeInfoProvider.GetInfo<DnvmCommand>()));
            return null;
        }
    }

    /// <summary>
    /// Throws an exception if the command was not recognized.
    /// </summary>
    public static CommandLineArguments ParseRaw(IAnsiConsole console, string[] args)
    {
        var dnvmCmd = CmdLine.ParseRaw<DnvmCommand>(args);
        if (dnvmCmd.Help == true)
        {
            console.WriteLine(CmdLine.GetHelpText(SerdeInfoProvider.GetInfo<DnvmCommand>()));
            return new CommandLineArguments(Command: null);
        }
        switch (dnvmCmd.Command)
        {
            case DnvmSubCommand.ListCommand c:
                if (CheckHelp<DnvmSubCommand.ListCommandProxy>(c.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                return new CommandLineArguments(new CommandArguments.ListArguments());
            case DnvmSubCommand.SelectCommand s:
                if (CheckHelp<DnvmSubCommand.SelectCommandProxy>(s.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                if (s.SdkDirName is not { } sdkDirName)
                {
                    throw new Serde.CmdLine.ArgumentSyntaxException("Missing required parameter: sdkDirName");
                }
                return new CommandLineArguments(new CommandArguments.SelectArguments { SdkDirName = sdkDirName });
            case DnvmSubCommand.UntrackCommand u:
                if (CheckHelp<DnvmSubCommand.UntrackCommandProxy>(u.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                if (u.Channel is null)
                {
                    throw new Serde.CmdLine.ArgumentSyntaxException("Missing required parameter: channel");
                }
                return new CommandLineArguments(new CommandArguments.UntrackArguments { Channel = u.Channel });
            case DnvmSubCommand.InstallCommand i:
                if (CheckHelp<DnvmSubCommand.InstallCommandProxy>(i.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                if (i.SdkVersion is null)
                {
                    throw new Serde.CmdLine.ArgumentSyntaxException("Missing required parameter: version");
                }
                return new CommandLineArguments(new CommandArguments.InstallArguments
                {
                    SdkVersion = i.SdkVersion,
                    Force = i.Force ?? false,
                    SdkDir = i.SdkDir,
                    Verbose = i.Verbose ?? false,
                });
            case DnvmSubCommand.TrackCommand t:
                if (CheckHelp<DnvmSubCommand.TrackCommandProxy>(t.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                if (t.Channel is null)
                {
                    throw new Serde.CmdLine.ArgumentSyntaxException("Missing required parameter: channel");
                }
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
            case DnvmSubCommand.SelfInstallCommand s:
                if (CheckHelp<DnvmSubCommand.SelfInstallCommandProxy>(s.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                return new CommandLineArguments(new CommandArguments.SelfInstallArguments
                {
                    Verbose = s.Verbose ?? false,
                    Yes = s.Yes ?? false,
                    FeedUrl = s.FeedUrl,
                    Force = s.Force ?? false,
                    Update = s.Update ?? false,
                });
            case DnvmSubCommand.UpdateCommand u:
                if (CheckHelp<DnvmSubCommand.UpdateCommandProxy>(u.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                return new CommandLineArguments(new CommandArguments.UpdateArguments
                {
                    Self = u.Self ?? false,
                    Verbose = u.Verbose ?? false,
                    FeedUrl = u.FeedUrl,
                    DnvmReleasesUrl = u.DnvmReleasesUrl,
                });
            case DnvmSubCommand.UninstallCommand u:
                if (CheckHelp<DnvmSubCommand.UninstallCommandProxy>(u.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                if (u.SdkVersion is null)
                {
                    throw new Serde.CmdLine.ArgumentSyntaxException("Missing required parameter: sdkVersion");
                }
                return new CommandLineArguments(new CommandArguments.UninstallArguments
                {
                    SdkVersion = u.SdkVersion,
                    Dir = u.SdkDir,
                });
            case DnvmSubCommand.PruneCommand p:
                if (CheckHelp<DnvmSubCommand.PruneCommandProxy>(p.Help, console))
                {
                    return new CommandLineArguments(Command: null);
                }
                return new CommandLineArguments(new CommandArguments.PruneArguments
                {
                    Verbose = p.Verbose ?? false,
                    DryRun = p.DryRun ?? false,
                });
            case null:
                console.WriteLine(CmdLine.GetHelpText(SerdeInfoProvider.GetInfo<DnvmCommand>()));
                return new CommandLineArguments(Command: null);
        }

        throw new InvalidOperationException("Unknown command");

        static bool CheckHelp<TCmdProxy>(bool? help, IAnsiConsole console)
            where TCmdProxy : ISerdeInfoProvider
        {
            if (help == true)
            {
                console.WriteLine(CmdLine.GetHelpText(
                    SerdeInfoProvider.GetInfo<TCmdProxy>(),
                    [SerdeInfoProvider.GetInfo<DnvmCommand>()]));
                return true;
            }
            return false;
        }
    }
}