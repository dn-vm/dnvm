
using System;
using Semver;
using Serde;

namespace Dnvm;

/// <summary>
/// Serializes as a string.
/// </summary>
internal sealed class SemVersionProxy : ISerialize<SemVersion>, IDeserialize<SemVersion>,
    ISerializeProvider<SemVersion>, IDeserializeProvider<SemVersion>
{
    public static readonly SemVersionProxy Instance = new();
    static ISerialize<SemVersion> ISerializeProvider<SemVersion>.SerializeInstance => Instance;
    static IDeserialize<SemVersion> IDeserializeProvider<SemVersion>.DeserializeInstance => Instance;

    private SemVersionProxy() { }

    public static ISerdeInfo SerdeInfo { get; } = Serde.SerdeInfo.MakePrimitive(nameof(SemVersion));

    public SemVersion Deserialize(IDeserializer deserializer)
    {
        var str = StringProxy.Instance.Deserialize(deserializer);
        if (SemVersion.TryParse(str, SemVersionStyles.Strict, out var version))
        {
            return version;
        }
        throw new DeserializeException($"Version string '{str}' is not a valid SemVersion.");
    }

    public void Serialize(SemVersion value, ISerializer serializer)
    {
        serializer.SerializeString(value.ToString());
    }
}