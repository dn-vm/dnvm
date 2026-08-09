using System;
using System.Linq;
using Semver;
using Serde;
using StaticCs.Collections;

namespace Dnvm;

[GenerateSerde(As = typeof(string))]
public sealed partial record SdkDirNameV10(string Name)
{
    public string Name { get; init; } = Name.ToLower();

    public static implicit operator string(SdkDirNameV10 dirName) => dirName.Name;
    public static implicit operator SdkDirNameV10(string name) => new(name);
    public static implicit operator SdkDirNameV10(SdkDirNameV9 dirName) => new(dirName.Name);
}

[GenerateSerde]
public sealed partial record ManifestV10
{
    public const int VersionField = 10;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;

    public required bool PreviewsEnabled { get; init; }
    public required SdkDirNameV10 CurrentSdkDir { get; init; }
    public required EqArray<InstalledSdkV10> InstalledSdks { get; init; }
    public required EqArray<RegisteredChannelV10> RegisteredChannels { get; init; }
}

[GenerateSerde]
[UseProxy(ForType = typeof(SemVersion), Proxy = typeof(SemVersionProxy))]
public partial record RegisteredChannelV10
{
    public required Channel ChannelName { get; init; }
    public required SdkDirNameV10 SdkDirName { get; init; }
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
    public bool Untracked { get; init; } = false;
}

[GenerateSerde]
[UseProxy(ForType = typeof(SemVersion), Proxy = typeof(SemVersionProxy))]
public partial record InstalledSdkV10
{
    public required SemVersion ReleaseVersion { get; init; }
    public required SemVersion SdkVersion { get; init; }
    public required SemVersion RuntimeVersion { get; init; }
    public required SemVersion AspNetVersion { get; init; }
    [SerdeMemberOptions(
        SerializeProxy = typeof(NullableRefProxy.Ser<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(NullableRefProxy.De<SemVersion, SemVersionProxy>))]
    public required SemVersion? WindowsDesktopVersion { get; init; }
    public EqArray<string> SdkManifestBands { get; init; } = EqArray<string>.Empty;
    public bool SdkManifestBandsKnown { get; init; } = false;
    public required SdkDirNameV10 SdkDirName { get; init; }
}

public static partial class ManifestV10Convert
{
    public static ManifestV10 Convert(this ManifestV9 v9) => new()
    {
        PreviewsEnabled = v9.PreviewsEnabled,
        CurrentSdkDir = v9.CurrentSdkDir,
        InstalledSdks = v9.InstalledSdks.SelectAsArray(v => v.Convert()),
        RegisteredChannels = v9.RegisteredChannels.SelectAsArray(c => c.Convert())
    };

    public static InstalledSdkV10 Convert(this InstalledSdkV9 v9) => new()
    {
        ReleaseVersion = v9.ReleaseVersion,
        SdkVersion = v9.SdkVersion,
        RuntimeVersion = v9.RuntimeVersion,
        AspNetVersion = v9.AspNetVersion,
        WindowsDesktopVersion = null,
        SdkManifestBandsKnown = false,
        SdkDirName = v9.SdkDirName,
    };

    public static RegisteredChannelV10 Convert(this RegisteredChannelV9 v9) => new()
    {
        ChannelName = v9.ChannelName,
        SdkDirName = v9.SdkDirName,
        InstalledSdkVersions = v9.InstalledSdkVersions,
        Untracked = v9.Untracked,
    };
}
