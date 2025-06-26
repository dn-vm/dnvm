
using System;
using System.Linq;
using Semver;
using Serde;
using StaticCs.Collections;

namespace Dnvm;

/// <summary>
/// Serializes the <see cref="SdkDirName.Name"/> directly as a string.
/// </summary>
[GenerateSerde(With = typeof(SdkDirNameV9._SerdeObj))]
public sealed partial record SdkDirNameV9(string Name)
{
    public string Name { get; init; } = Name.ToLower();

    private sealed class _SerdeObj : ISerde<SdkDirNameV9>
    {
        public ISerdeInfo SerdeInfo => StringProxy.SerdeInfo;
        public SdkDirNameV9 Deserialize(IDeserializer deserializer)
            => new SdkDirNameV9(StringProxy.Instance.Deserialize(deserializer));
        public void Serialize(SdkDirNameV9 value, ISerializer serializer)
        {
            serializer.WriteString(value.Name);
        }
    }

    public static implicit operator SdkDirNameV9(SdkDirNameV8 dirName) => new SdkDirNameV9(dirName.Name);
}

[GenerateSerde]
public sealed partial record ManifestV9
{
    // Serde doesn't serialize consts, so we have a separate property below for serialization.
    public const int VersionField = 9;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => VersionField;

    public required bool PreviewsEnabled { get; init; }
    public required SdkDirNameV9 CurrentSdkDir { get; init; }
    public required EqArray<InstalledSdkV9> InstalledSdks { get; init; }
    public required EqArray<RegisteredChannelV9> RegisteredChannels { get; init; }
}

[GenerateSerde]
public partial record RegisteredChannelV9
{
    public required Channel ChannelName { get; init; }
    public required SdkDirNameV9 SdkDirName { get; init; }
    [SerdeMemberOptions(
        SerializeProxy = typeof(EqArrayProxy.Ser<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(EqArrayProxy.De<SemVersion, SemVersionProxy>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
    public bool Untracked { get; init; } = false;
}

[GenerateSerde]
public partial record InstalledSdkV9
{
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion ReleaseVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion SdkVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion RuntimeVersion { get; init; }
    [SerdeMemberOptions(Proxy = typeof(SemVersionProxy))]
    public required SemVersion AspNetVersion { get; init; }
    public required SdkDirNameV9 SdkDirName { get; init; }
}

public static partial class ManifestV9Convert
{
    public static ManifestV9 Convert(this ManifestV8 v8) => new ManifestV9
    {
        PreviewsEnabled = false,
        CurrentSdkDir = v8.CurrentSdkDir,
        InstalledSdks = v8.InstalledSdks.SelectAsArray(v => v.Convert()),
        RegisteredChannels = v8.RegisteredChannels.SelectAsArray(c => c.Convert())
    };

    public static InstalledSdkV9 Convert(this InstalledSdkV8 v8) => new InstalledSdkV9
    {
        ReleaseVersion = v8.ReleaseVersion,
        SdkVersion = v8.SdkVersion,
        RuntimeVersion = v8.RuntimeVersion,
        AspNetVersion = v8.AspNetVersion,
        SdkDirName = v8.SdkDirName,
    };

    public static RegisteredChannelV9 Convert(this RegisteredChannelV8 v8) => new RegisteredChannelV9
    {
        ChannelName = v8.ChannelName,
        SdkDirName = v8.SdkDirName,
        InstalledSdkVersions = v8.InstalledSdkVersions,
        Untracked = v8.Untracked,
    };
}