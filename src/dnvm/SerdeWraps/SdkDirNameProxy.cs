
using Serde;

namespace Dnvm;

/// <summary>
/// Serializes the <see cref="SdkDirName.Name"/> directly as a string.
/// </summary>
internal sealed class SdkDirNameProxy : ISerialize<SdkDirName>, IDeserialize<SdkDirName>
{
    public static ISerdeInfo SerdeInfo { get; } = Serde.SerdeInfo.MakePrimitive(nameof(SdkDirName));

    public static SdkDirName Deserialize(IDeserializer deserializer)
        => new SdkDirName(StringWrap.Deserialize(deserializer));

    public void Serialize(SdkDirName value, ISerializer serializer)
    {
        serializer.SerializeString(value.Name);
    }
}