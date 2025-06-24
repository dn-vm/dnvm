
using System;
using System.Linq;
using Semver;
using Serde;
using StaticCs.Collections;

namespace Dnvm;

/// <summary>
/// Serializes the <see cref="SdkDirName.Name"/> directly as a string.
/// </summary>
internal sealed class SdkDirNameV8 : ISerde<SdkDirName>, ISerdeProvider<SdkDirNameV8, SdkDirNameV8, SdkDirName>
{
    public static SdkDirNameV8 Instance { get; } = new();
    public ISerdeInfo SerdeInfo => StringProxy.SerdeInfo;

    public SdkDirName Deserialize(IDeserializer deserializer)
        => new SdkDirName(StringProxy.Instance.Deserialize(deserializer));

    public void Serialize(SdkDirName value, ISerializer serializer)
    {
        serializer.WriteString(value.Name);
    }
}

[GenerateSerde]
public sealed partial record ManifestV8(
    bool PreviewsEnabled,
    SdkDirName CurrentSdkDir,
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
    public required SdkDirName SdkDirName { get; init; }
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

    public SdkDirName SdkDirName { get; init; } = DnvmEnv.DefaultSdkDirName;
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