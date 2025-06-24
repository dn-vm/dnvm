
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using StaticCs.Collections;

namespace Dnvm;

/// <summary>
/// Holds the simple name of a directory that contains one or more SDKs and lives under DNVM_HOME.
/// This is a wrapper to prevent being used directly as a path.
/// </summary>
public sealed record SdkDirName(string Name)
{
    public string Name { get; init; } = Name.ToLower();
}

public partial record RegisteredChannel
{
    public required Channel ChannelName { get; init; }
    public required SdkDirName SdkDirName { get; init; }
    [SerdeMemberOptions(
        SerializeProxy = typeof(EqArrayProxy.Ser<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(EqArrayProxy.De<SemVersion, SemVersionProxy>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
    public bool Untracked { get; init; } = false;
}

public partial record InstalledSdk
{
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion ReleaseVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion SdkVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion RuntimeVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion AspNetVersion { get; init; }

    public SdkDirName SdkDirName { get; init; } = DnvmEnv.DefaultSdkDirName;
}

public sealed partial record Manifest
{
    public static readonly Manifest Empty = new();

    public bool PreviewsEnabled { get; init; } = false;
    public SdkDirName CurrentSdkDir { get; init; } = DnvmEnv.DefaultSdkDirName;
    public EqArray<InstalledSdk> InstalledSdks { get; init; } = [];
    public EqArray<RegisteredChannel> RegisteredChannels { get; init; } = [];
}

partial record Manifest
{
    public EqArray<RegisteredChannel> TrackedChannels()
    {
        return RegisteredChannels.Where(x => !x.Untracked).ToEq();
    }

    /// <summary>
    /// Calculates the version of the installed muxer. This is
    /// Max(<all installed _runtime_ versions>).
    /// If no SDKs are installed, returns null.
    /// </summary>
    public SemVersion? MuxerVersion(SdkDirName dir)
    {
        var installedSdks = InstalledSdks
            .Where(s => s.SdkDirName == dir)
            .ToList();
        if (installedSdks.Count == 0)
        {
            return null;
        }
        return installedSdks
            .Select(s => s.RuntimeVersion)
            .Max(SemVersion.SortOrderComparer);
    }

    public Manifest AddSdk(
        SemVersion semVersion,
        Channel? c = null,
        SdkDirName? sdkDirParam = null)
    {
        if (sdkDirParam is not { } sdkDir)
        {
            sdkDir = DnvmEnv.DefaultSdkDirName;
        }
        var installedSdk = new InstalledSdk()
        {
            SdkDirName = sdkDir,
            SdkVersion = semVersion,
            RuntimeVersion = semVersion,
            AspNetVersion = semVersion,
            ReleaseVersion = semVersion,
        };
        return AddSdk(installedSdk, c);
    }

    public Manifest AddSdk(InstalledSdk sdk, Channel? c = null)
    {
        var installedSdks = this.InstalledSdks;
        if (!installedSdks.Contains(sdk))
        {
            installedSdks = installedSdks.Add(sdk);
        }
        EqArray<RegisteredChannel> allChannels = this.RegisteredChannels;
        if (allChannels.FirstOrNull(x => !x.Untracked && x.ChannelName == c && x.SdkDirName == sdk.SdkDirName) is { } oldTracked)
        {
            var installedSdkVersions = oldTracked.InstalledSdkVersions;
            var newTracked = installedSdkVersions.Contains(sdk.SdkVersion)
                ? oldTracked
                : oldTracked with
                {
                    InstalledSdkVersions = installedSdkVersions.Add(sdk.SdkVersion)
                };
            allChannels = allChannels.Replace(oldTracked, newTracked);
        }
        else if (c is not null)
        {
            allChannels = allChannels.Add(new RegisteredChannel
            {
                ChannelName = c,
                SdkDirName = sdk.SdkDirName,
                InstalledSdkVersions = [sdk.SdkVersion]
            });
        }
        return this with
        {
            InstalledSdks = installedSdks,
            RegisteredChannels = allChannels,
        };
    }

    public bool IsSdkInstalled(SemVersion version, SdkDirName dirName)
    {
        return this.InstalledSdks.Any(s => s.SdkVersion == version && s.SdkDirName == dirName);
    }

    public Manifest TrackChannel(RegisteredChannel channel)
    {
        var existing = RegisteredChannels.FirstOrNull(c =>
            c.ChannelName == channel.ChannelName && c.SdkDirName == channel.SdkDirName);
        if (existing is null)
        {
            return this with
            {
                RegisteredChannels = RegisteredChannels.Add(channel)
            };
        }
        else if (existing is { Untracked: true })
        {
            var newVersions = existing.InstalledSdkVersions.Concat(channel.InstalledSdkVersions).Distinct().ToEq();
            return this with
            {
                RegisteredChannels = RegisteredChannels.Replace(existing, existing with
                {
                    InstalledSdkVersions = newVersions,
                    Untracked = false,
                })
            };
        }
        throw new InvalidOperationException("Channel already tracked");
    }

    internal Manifest UntrackChannel(Channel channel)
    {
        return this with
        {
            RegisteredChannels = RegisteredChannels.Select(c =>
            {
                if (c.ChannelName == channel)
                {
                    return c with { Untracked = true };
                }
                return c;
            }).ToEq()
        };
    }
}

