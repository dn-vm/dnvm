
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
public sealed partial record SdkDirNameV8(string Name)
{
    public string Name { get; init; } = Name.ToLower();
    public static implicit operator SdkDirNameV8(SdkDirNameV7 dirName) => new SdkDirNameV8(dirName.Name);
}

[GenerateSerde]
public sealed partial record ManifestV8(
    bool PreviewsEnabled,
    SdkDirNameV8 CurrentSdkDir,
    EqArray<InstalledSdkV8> InstalledSdks,
    EqArray<RegisteredChannelV8> RegisteredChannels
)
{
    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 8;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;
}

[GenerateSerde]
public partial record RegisteredChannelV8
{
    public required Channel ChannelName { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SdkDirNameV8))]
    public required SdkDirNameV8 SdkDirName { get; init; }
    [SerdeMemberOptions(
        SerializeProxy = typeof(EqArrayProxy.Ser<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(EqArrayProxy.De<SemVersion, SemVersionProxy>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
    public bool Untracked { get; init; } = false;
}

[GenerateSerde]
public partial record InstalledSdkV8
{
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion ReleaseVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion SdkVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion RuntimeVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion AspNetVersion { get; init; }

    [SerdeMemberOptions(Proxy = typeof(SdkDirNameV8))]
    public required SdkDirNameV8 SdkDirName { get; init; }
}

public static partial class ManifestV8Convert
{
    public static ManifestV8 Convert(this ManifestV7 v7) => new ManifestV8
    (
        PreviewsEnabled: false,
        CurrentSdkDir: v7.CurrentSdkDir,
        InstalledSdks: v7.InstalledSdks.SelectAsArray(v => v.Convert()),
        RegisteredChannels: v7.RegisteredChannels.SelectAsArray(c => c.Convert())
    );

    public static InstalledSdkV8 Convert(this InstalledSdkV7 v7) => new InstalledSdkV8
    {
        ReleaseVersion = v7.ReleaseVersion,
        SdkVersion = v7.SdkVersion,
        RuntimeVersion = v7.RuntimeVersion,
        AspNetVersion = v7.AspNetVersion,
        SdkDirName = v7.SdkDirName,
    };

    public static RegisteredChannelV8 Convert(this RegisteredChannelV7 v7) => new RegisteredChannelV8
    {
        ChannelName = v7.ChannelName,
        SdkDirName = v7.SdkDirName,
        InstalledSdkVersions = v7.InstalledSdkVersions,
        Untracked = v7.Untracked,
    };
}