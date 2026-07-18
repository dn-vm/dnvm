using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Spectre.Console;
using StaticCs;

namespace Dnvm;

public static class WindowsSystemCommands
{
    public static Task<int> Run(DnvmEnv env, Logger logger, DnvmSubCommand command)
        => command switch
        {
            DnvmSubCommand.InstallArgs args => Install(env, logger, args),
            DnvmSubCommand.TrackArgs args => Track(env, logger, args),
            DnvmSubCommand.UpdateArgs args => Update(env, logger, args),
            DnvmSubCommand.ListArgs => List(env),
            DnvmSubCommand.ListRemoteArgs args => ListRemoteCommand.Run(env, args),
            DnvmSubCommand.SelectArgs => UnsupportedSelect(env),
            DnvmSubCommand.UntrackArgs args => Untrack(env, args.Channel),
            DnvmSubCommand.UninstallArgs args => Uninstall(env, logger, args),
            DnvmSubCommand.PruneArgs args => Prune(env, logger, args),
            DnvmSubCommand.RestoreArgs args => Restore(env, logger, args),
            DnvmSubCommand.SelfInstallArgs args => SelfInstall(env, logger, args),
            _ => throw ExceptionUtilities.Unreachable,
        };

    public static async Task<int> EnableDnvmPreviews(DnvmEnv env)
    {
        using var @lock = await WindowsPolicyLock.Acquire(env);
        var policy = @lock.ReadOrCreate(env) with { PreviewsEnabled = true };
        @lock.Write(env, policy);
        return 0;
    }

    private static async Task<int> Install(
        DnvmEnv env,
        Logger logger,
        DnvmSubCommand.InstallArgs args)
    {
        if (args.Dir is not null)
        {
            return (int)await InstallCommand.Run(env, logger, args);
        }
        if (args.SdkDir is not null)
        {
            env.Console.Error("--sdk-dir is only available in user scope.");
            return 1;
        }

        var releaseIndex = await FetchReleaseIndex(env);
        if (releaseIndex is null)
        {
            return 1;
        }
        var release = await InstallCommand.TryGetReleaseFromIndex(
            env.HttpClient,
            releaseIndex,
            new Channel.VersionedMajorMinor(args.SdkVersion.Major, args.SdkVersion.Minor),
            args.SdkVersion)
            ?? await InstallCommand.TryGetReleaseFromServer(env, args.SdkVersion);
        if (release is not ({ } component, _))
        {
            env.Console.Error($"SDK version '{args.SdkVersion}' could not be found.");
            return 1;
        }

        return await InstallComponent(env, logger, component, args.Force ?? false);
    }

    private static async Task<int> Track(
        DnvmEnv env,
        Logger logger,
        DnvmSubCommand.TrackArgs args)
    {
        if (args.SdkDir is not null)
        {
            env.Console.Error("--sdk-dir is only available in user scope.");
            return 1;
        }

        using var @lock = await WindowsPolicyLock.Acquire(env);
        var policy = @lock.ReadOrCreate(env);
        if (policy.TrackedChannels().Any(x => x.ChannelName == args.Channel))
        {
            env.Console.Warn($"Channel '{args.Channel.GetDisplayName()}' is already being tracked.");
            return 0;
        }

        var releaseIndex = await FetchReleaseIndex(env, args.FeedUrl);
        if (releaseIndex is null)
        {
            return 1;
        }
        var latest = releaseIndex.GetChannelIndex(args.Channel);
        if (latest is null)
        {
            env.Console.Error($"No builds are currently available for channel '{args.Channel}'.");
            return 1;
        }
        var version = SemVersion.Parse(latest.LatestSdk, SemVersionStyles.Strict);
        var release = await InstallCommand.TryGetReleaseFromIndex(
            env.HttpClient,
            releaseIndex,
            args.Channel,
            version);
        if (release is not ({ } component, _))
        {
            env.Console.Error($"The release index does not contain SDK '{version}'.");
            return 1;
        }

        if (await InstallComponent(env, logger, component, args.Force ?? false) != 0)
        {
            return 1;
        }

        policy = policy.Track(args.Channel).RecordInstallation(args.Channel, version);
        @lock.Write(env, policy);
        env.Console.WriteLine($"Tracking channel '{args.Channel.GetDisplayName()}'.");
        return 0;
    }

    private static async Task<int> Update(
        DnvmEnv env,
        Logger logger,
        DnvmSubCommand.UpdateArgs args)
    {
        using var @lock = await WindowsPolicyLock.Acquire(env);
        var policy = @lock.ReadOrCreate(env);
        if (args.Self == true)
        {
            var updater = new UpdateCommand(env, logger, new UpdateCommand.Options
            {
                DnvmReleasesUrl = args.DnvmReleasesUrl,
                FeedUrl = args.FeedUrl,
                Verbose = args.Verbose ?? false,
                Self = true,
                Yes = args.Yes ?? false,
            });
            var result = await updater.UpdateSelf(new Manifest
            {
                PreviewsEnabled = policy.PreviewsEnabled,
            });
            return (int)result;
        }

        var releaseIndex = await FetchReleaseIndex(env, args.FeedUrl);
        if (releaseIndex is null)
        {
            return 1;
        }

        var updates = new List<(WindowsTrackedChannel Tracked, SemVersion Version)>();
        foreach (var tracked in policy.TrackedChannels())
        {
            var latest = releaseIndex.GetChannelIndex(tracked.ChannelName);
            if (latest is null
                || !SemVersion.TryParse(latest.LatestSdk, SemVersionStyles.Strict, out var version))
            {
                continue;
            }
            var newestRecorded = tracked.InstalledSdkVersions.Max(SemVersion.PrecedenceComparer);
            if (newestRecorded is null
                || SemVersion.ComparePrecedence(newestRecorded, version) < 0)
            {
                updates.Add((tracked, version));
            }
        }

        if (updates.Count == 0)
        {
            env.Console.WriteLine("All tracked channels are up to date.");
            return 0;
        }

        var table = new Table().AddColumn("Channel").AddColumn("Available");
        foreach (var update in updates)
        {
            table.AddRow(update.Tracked.ChannelName.GetDisplayName(), update.Version.ToString());
        }
        env.Console.Write(table);
        if (!Confirm("Install updates?", args.Yes ?? false))
        {
            return 0;
        }

        foreach (var update in updates)
        {
            var release = await InstallCommand.TryGetReleaseFromIndex(
                env.HttpClient,
                releaseIndex,
                update.Tracked.ChannelName,
                update.Version);
            if (release is not ({ } component, _)
                || await InstallComponent(env, logger, component, force: false) != 0)
            {
                return 1;
            }
            policy = policy.RecordInstallation(update.Tracked.ChannelName, update.Version);
        }
        @lock.Write(env, policy);
        return 0;
    }

    private static async Task<int> List(DnvmEnv env)
    {
        using var @lock = await WindowsPolicyLock.Acquire(env);
        var policy = @lock.ReadOrCreate(env);
        var installations = GetBackend(env).GetInstalledSdks();

        env.Console.WriteLine($"System .NET location: {GetBackend(env).DotnetInstallLocation}");
        env.Console.WriteLine();
        var table = new Table()
            .AddColumn("Version")
            .AddColumn("Architecture")
            .AddColumn("Location")
            .AddColumn("Registration")
            .AddColumn("Removable");
        foreach (var installation in installations)
        {
            table.AddRow(
                installation.Version.ToString(),
                installation.Architecture,
                installation.InstallLocation,
                installation.RegistrationSource,
                installation.IsUninstallable && !installation.IsVisualStudioManaged ? "yes" : "no");
        }
        env.Console.Write(table);
        env.Console.WriteLine();
        env.Console.WriteLine("Tracked channels:");
        foreach (var tracked in policy.TrackedChannels())
        {
            env.Console.WriteLine($" • {tracked.ChannelName.GetLowerName()}");
        }
        return 0;
    }

    private static async Task<int> Untrack(DnvmEnv env, Channel channel)
    {
        using var @lock = await WindowsPolicyLock.Acquire(env);
        var policy = @lock.ReadOrCreate(env);
        if (!policy.TrackedChannels().Any(x => x.ChannelName == channel))
        {
            env.Console.Error($"Channel '{channel}' is not tracked.");
            return 1;
        }
        @lock.Write(env, policy.Untrack(channel));
        return 0;
    }

    private static async Task<int> Uninstall(
        DnvmEnv env,
        Logger logger,
        DnvmSubCommand.UninstallArgs args)
    {
        if (args.SdkDir is not null)
        {
            env.Console.Error("--sdk-dir is only available in user scope.");
            return 1;
        }

        var matches = GetBackend(env).GetInstalledSdks()
            .Where(x => x.Version == args.SdkVersion)
            .ToList();
        if (matches.Count == 0)
        {
            env.Console.Error($"SDK version {args.SdkVersion} is not installed.");
            return 1;
        }
        if (matches.Any(x => !x.IsUninstallable || x.IsVisualStudioManaged))
        {
            env.Console.Error(
                "Windows does not expose this SDK as independently removable. "
                + "It may be owned by Visual Studio or another installer.");
            return 1;
        }
        if (!Confirm(
            $"Remove globally shared SDK {args.SdkVersion} for all users?",
            args.Yes ?? false))
        {
            return 1;
        }

        foreach (var installation in matches)
        {
            var result = await GetBackend(env).Uninstall(env, logger, installation);
            if (ReportSystemResult(env, result) != 0)
            {
                return 1;
            }
        }

        using var @lock = await WindowsPolicyLock.Acquire(env);
        @lock.Write(env, @lock.ReadOrCreate(env).RemoveVersion(args.SdkVersion));
        return 0;
    }

    private static async Task<int> Prune(
        DnvmEnv env,
        Logger logger,
        DnvmSubCommand.PruneArgs args)
    {
        using var @lock = await WindowsPolicyLock.Acquire(env);
        var policy = @lock.ReadOrCreate(env);
        var candidates = GetPruneCandidates(policy);
        var inventory = GetBackend(env).GetInstalledSdks();
        foreach (var version in candidates)
        {
            var matches = inventory.Where(x => x.Version == version).ToList();
            if (matches.Count == 0)
            {
                policy = policy.RemoveVersion(version);
                continue;
            }
            if (matches.Any(x => !x.IsUninstallable || x.IsVisualStudioManaged))
            {
                env.Console.Warn($"Skipping {version}: Windows does not expose it as independently removable.");
                continue;
            }
            if (args.DryRun == true)
            {
                env.Console.WriteLine($"Would remove system SDK {version}");
                continue;
            }
            if (!Confirm($"Remove globally shared SDK {version} for all users?", args.Yes ?? false))
            {
                continue;
            }
            foreach (var installation in matches)
            {
                if (ReportSystemResult(
                    env,
                    await GetBackend(env).Uninstall(env, logger, installation)) != 0)
                {
                    return 1;
                }
            }
            policy = policy.RemoveVersion(version);
        }
        @lock.Write(env, policy);
        return 0;
    }

    private static async Task<int> Restore(
        DnvmEnv env,
        Logger logger,
        DnvmSubCommand.RestoreArgs args)
    {
        var result = await RestoreCommand.Run(env, logger, args);
        return result switch
        {
            Result<SemVersion, RestoreCommand.Error>.Ok => 0,
            Result<SemVersion, RestoreCommand.Error>.Err error => (int)error.Value,
            _ => throw ExceptionUtilities.Unreachable,
        };
    }

    private static async Task<int> SelfInstall(
        DnvmEnv env,
        Logger logger,
        DnvmSubCommand.SelfInstallArgs args)
        => (int)await SelfInstallCommand.Run(env, logger, args);

    private static Task<int> UnsupportedSelect(DnvmEnv env)
    {
        env.Console.Error(
            "The system-wide Windows installation uses the canonical dotnet location; "
            + "'select' is only available in user scope.");
        return Task.FromResult(1);
    }

    internal static async Task<int> InstallComponent(
        DnvmEnv env,
        Logger logger,
        ChannelReleaseIndex.Component component,
        bool force)
    {
        var backend = GetBackend(env);
        if (!force && backend.GetInstalledSdks().Any(x => x.Version == component.Version))
        {
            env.Console.WriteLine($"SDK {component.Version} is already installed system-wide.");
            return 0;
        }

        env.Console.WriteLine($"Installing SDK {component.Version} for all users.");
        return ReportSystemResult(env, await backend.Install(env, logger, component));
    }

    internal static IReadOnlyList<SemVersion> GetPruneCandidates(WindowsPolicy policy)
    {
        var keep = new HashSet<SemVersion>();
        var all = new HashSet<SemVersion>();
        foreach (var tracked in policy.TrackedChannels())
        {
            foreach (var group in tracked.InstalledSdkVersions.GroupBy(x => x.ToMajorMinor()))
            {
                foreach (var version in group)
                {
                    all.Add(version);
                }
                var latest = group.Max(SemVersion.SortOrderComparer);
                if (latest is not null)
                {
                    keep.Add(latest);
                }
            }
        }
        all.ExceptWith(keep);
        return all.OrderBy(x => x, SemVersion.SortOrderComparer).ToList();
    }

    private static async Task<DotnetReleasesIndex?> FetchReleaseIndex(
        DnvmEnv env,
        string? feedUrl = null)
    {
        try
        {
            return await DotnetReleasesIndex.FetchLatestIndex(
                env.HttpClient,
                feedUrl is null ? env.DotnetFeedUrls : [feedUrl.TrimEnd('/')]);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            env.Console.Error($"Could not fetch the releases index: {e.Message}");
            return null;
        }
    }

    private static ISystemInstallBackend GetBackend(DnvmEnv env)
        => env.SystemInstallBackend
            ?? throw new InvalidOperationException("System installation backend is not configured.");

    private static int ReportSystemResult(DnvmEnv env, SystemInstallResult result)
    {
        switch (result)
        {
            case SystemInstallResult.Success:
                return 0;
            case SystemInstallResult.RequiresElevation:
                env.Console.Error("System-wide .NET changes require an elevated Administrator terminal.");
                break;
            case SystemInstallResult.InstallerUnavailable:
                env.Console.Error("The Microsoft release metadata does not contain a Windows installer bundle for this SDK.");
                break;
            case SystemInstallResult.DownloadFailed:
                env.Console.Error("Failed to download the official .NET SDK installer.");
                break;
            case SystemInstallResult.IntegrityCheckFailed:
                env.Console.Error("The official .NET SDK installer failed integrity verification.");
                break;
            case SystemInstallResult.InstallerFailed:
                env.Console.Error("The registered Windows installer operation failed.");
                break;
            case SystemInstallResult.RegistrationNotFound:
                env.Console.Error("The installer completed, but Windows did not report the requested SDK.");
                break;
            case SystemInstallResult.UninstallRefused:
                env.Console.Error("Windows does not expose this SDK as safely and independently removable.");
                break;
        }
        return 1;
    }

    private static bool Confirm(string prompt, bool yes)
    {
        if (yes)
        {
            return true;
        }
        Console.Write($"{prompt} [y/N]: ");
        return Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
    }
}
