
using System;
using System.Linq;
using Semver;
using Serde;
using StaticCs.Collections;

namespace Dnvm;

public sealed partial record Manifest
{
    public static readonly Manifest Empty = new();

    public bool PreviewsEnabled { get; init; } = false;
    public SdkDirName CurrentSdkDir { get; init; } = DnvmEnv.DefaultSdkDirName;
    public EqArray<InstalledSdk> InstalledSdks { get; init; } = [];
    public EqArray<RegisteredChannel> RegisteredChannels { get; init; } = [];
}

public static class ManifestConvert
{
    public static Manifest Convert(this ManifestV8 manifestV8)
    {
        return new Manifest
        {
            PreviewsEnabled = manifestV8.PreviewsEnabled,
            CurrentSdkDir = manifestV8.CurrentSdkDir,
            InstalledSdks = manifestV8.InstalledSdks.SelectAsArray(sdk => new InstalledSdk
            {
                ReleaseVersion = sdk.ReleaseVersion,
                SdkVersion = sdk.SdkVersion,
                RuntimeVersion = sdk.RuntimeVersion,
                AspNetVersion = sdk.AspNetVersion,
                SdkDirName = sdk.SdkDirName
            }),
            RegisteredChannels = manifestV8.RegisteredChannels.SelectAsArray(channel => new RegisteredChannel
            {
                ChannelName = channel.ChannelName,
                SdkDirName = channel.SdkDirName,
                InstalledSdkVersions = channel.InstalledSdkVersions.ToEq(),
                Untracked = channel.Untracked
            })
        };
    }
}

public sealed partial record Manifest
{
    public ManifestV8 ToManifestV8()
    {
        return new ManifestV8
        (
            PreviewsEnabled: PreviewsEnabled,
            CurrentSdkDir: CurrentSdkDir,
            InstalledSdks: InstalledSdks.SelectAsArray(sdk => new InstalledSdkV8
            {
                ReleaseVersion = sdk.ReleaseVersion,
                SdkVersion = sdk.SdkVersion,
                RuntimeVersion = sdk.RuntimeVersion,
                AspNetVersion = sdk.AspNetVersion,
                SdkDirName = sdk.SdkDirName
            }),
            RegisteredChannels: RegisteredChannels.SelectAsArray(channel => new RegisteredChannelV8
            {
                ChannelName = channel.ChannelName,
                SdkDirName = channel.SdkDirName,
                InstalledSdkVersions = channel.InstalledSdkVersions.ToEq(),
                Untracked = channel.Untracked
            })
        );
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