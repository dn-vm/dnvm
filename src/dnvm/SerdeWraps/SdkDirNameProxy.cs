
using Serde;

namespace Dnvm;

/// <summary>
/// Serializes the <see cref="SdkDirName.Name"/> directly as a string.
/// </summary>
internal sealed class SdkDirNameProxy : ISerialize<SdkDirName>, IDeserialize<SdkDirName>,
    ISerializeProvider<SdkDirName>, IDeserializeProvider<SdkDirName>
{
    public static readonly SdkDirNameProxy Instance = new();
    static ISerialize<SdkDirName> ISerializeProvider<SdkDirName>.SerializeInstance => Instance;
    static IDeserialize<SdkDirName> IDeserializeProvider<SdkDirName>.DeserializeInstance => Instance;
    public static ISerdeInfo SerdeInfo { get; } = Serde.SerdeInfo.MakePrimitive(nameof(SdkDirName));

    public SdkDirName Deserialize(IDeserializer deserializer)
        => new SdkDirName(StringProxy.Instance.Deserialize(deserializer));

    public void Serialize(SdkDirName value, ISerializer serializer)
    {
        serializer.SerializeString(value.Name);
    }
}