
using System;
using System.Linq;
using Semver;
using Serde;
using StaticCs.Collections;

namespace Dnvm;

[GenerateSerde]
public sealed partial record Manifest
{
    public static readonly Manifest Empty = new();

    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 7;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;

    public SdkDirName CurrentSdkDir { get; init; } = DnvmEnv.DefaultSdkDirName;
    public EqArray<InstalledSdk> InstalledSdks { get; init; } = [];
    public EqArray<RegisteredChannel> RegisteredChannels { get; init; } = [];

    internal Manifest TrackChannel(RegisteredChannel channel)
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

[GenerateSerde]
public partial record RegisteredChannel
{
    public required Channel ChannelName { get; init; }
    public required SdkDirName SdkDirName { get; init; }
    [SerdeMemberOptions(
        SerializeProxy = typeof(EqArrayProxy.Serialize<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(EqArrayProxy.Deserialize<SemVersion, SemVersionProxy>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
    public bool Untracked { get; init; } = false;
}

[GenerateSerde]
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

public static partial class ManifestConvert
{
    public static Manifest Convert(this ManifestV6 v6) => new Manifest
    {
        InstalledSdks = v6.InstalledSdks.SelectAsArray(v => v.Convert()).ToEq(),
        RegisteredChannels = v6.TrackedChannels.SelectAsArray(c => c.Convert()).ToEq(),
    };

    public static InstalledSdk Convert(this InstalledSdkV6 v6) => new InstalledSdk {
        ReleaseVersion = v6.ReleaseVersion,
        SdkVersion = v6.SdkVersion,
        RuntimeVersion = v6.RuntimeVersion,
        AspNetVersion = v6.AspNetVersion,
        SdkDirName = v6.SdkDirName,
    };

    public static RegisteredChannel Convert(this TrackedChannelV6 v6) => new RegisteredChannel {
        ChannelName = v6.ChannelName,
        SdkDirName = v6.SdkDirName,
        InstalledSdkVersions = v6.InstalledSdkVersions,
        Untracked = v6.Untracked,
    };
}