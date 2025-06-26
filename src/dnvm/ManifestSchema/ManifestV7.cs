
using System;
using System.Linq;
using Semver;
using Serde;
using StaticCs.Collections;

namespace Dnvm;

/// <summary>
/// Holds the simple name of a directory that contains one or more SDKs and lives under DNVM_HOME.
/// This is a wrapper to prevent being used directly as a path.
/// </summary>
[GenerateSerde]
public sealed partial record SdkDirNameV7(string Name)
{
    public string Name { get; init; } = Name.ToLower();
    public static implicit operator SdkDirNameV7(SdkDirNameV6 dirName) => new SdkDirNameV7(dirName.Name);
}

[GenerateSerde]
public sealed partial record ManifestV7
{
    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 7;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;

    public required SdkDirNameV7 CurrentSdkDir { get; init; }
    public required EqArray<InstalledSdkV7> InstalledSdks { get; init; } = [];
    public required EqArray<RegisteredChannelV7> RegisteredChannels { get; init; } = [];

    internal ManifestV7 TrackChannel(RegisteredChannelV7 channel)
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

    internal ManifestV7 UntrackChannel(Channel channel)
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
public partial record RegisteredChannelV7
{
    public required Channel ChannelName { get; init; }
    public required SdkDirNameV7 SdkDirName { get; init; }
    [SerdeMemberOptions(
        SerializeProxy = typeof(EqArrayProxy.Ser<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(EqArrayProxy.De<SemVersion, SemVersionProxy>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
    public bool Untracked { get; init; } = false;
}

[GenerateSerde]
public partial record InstalledSdkV7
{
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion ReleaseVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion SdkVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion RuntimeVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion AspNetVersion { get; init; }
    public required SdkDirNameV7 SdkDirName { get; init; }
}

public static partial class ManifestV7Convert
{
    public static ManifestV7 Convert(this ManifestV6 v6) => new ManifestV7
    {
        InstalledSdks = v6.InstalledSdks.SelectAsArray(v => v.Convert()).ToEq(),
        RegisteredChannels = v6.TrackedChannels.SelectAsArray(c => c.Convert()).ToEq(),
        CurrentSdkDir = v6.CurrentSdkDir,
    };

    public static InstalledSdkV7 Convert(this InstalledSdkV6 v6) => new InstalledSdkV7 {
        ReleaseVersion = v6.ReleaseVersion,
        SdkVersion = v6.SdkVersion,
        RuntimeVersion = v6.RuntimeVersion,
        AspNetVersion = v6.AspNetVersion,
        SdkDirName = v6.SdkDirName,
    };

    public static RegisteredChannelV7 Convert(this TrackedChannelV6 v6) => new RegisteredChannelV7 {
        ChannelName = v6.ChannelName,
        SdkDirName = v6.SdkDirName,
        InstalledSdkVersions = v6.InstalledSdkVersions,
        Untracked = v6.Untracked,
    };
}