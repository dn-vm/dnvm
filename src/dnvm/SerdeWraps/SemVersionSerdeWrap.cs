
using Semver;
using Serde;

namespace Dnvm;

/// <summary>
/// Serializes as a string.
/// </summary>
internal readonly record struct SemVersionSerdeWrap : ISerialize<SemVersion>, IDeserialize<SemVersion>
{
    public static ISerdeInfo SerdeInfo { get; } = Serde.SerdeInfo.MakePrimitive(nameof(SemVersion));

    public static SemVersion Deserialize(IDeserializer deserializer)
    {
        var str = StringWrap.Deserialize(deserializer);
        if (SemVersion.TryParse(str, SemVersionStyles.Strict, out var version))
        {
            return version;
        }
        throw new InvalidDeserializeValueException($"Version string '{str}' is not a valid SemVersion.");
    }

    public void Serialize(SemVersion value, ISerializer serializer)
    {
        serializer.SerializeString(value.ToString());
    }
}