
using Serde;

namespace Dnvm;

internal sealed class SdkDirNameProxy : ISerialize<SdkDirName>, IDeserialize<SdkDirName>
{
    public static ISerdeInfo SerdeInfo { get; } = Serde.SerdeInfo.MakePrimitive(nameof(SdkDirName));

    public static SdkDirName Deserialize(IDeserializer deserializer)
    {
        var str = StringWrap.Deserialize(deserializer);
        return new SdkDirName(str);
    }

    public void Serialize(SdkDirName value, ISerializer serializer)
    {
        serializer.SerializeString(value.Name);
    }
}