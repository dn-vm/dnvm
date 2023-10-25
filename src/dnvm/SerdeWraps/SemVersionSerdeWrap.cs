
using Semver;
using Serde;

namespace Dnvm;

/// <summary>
/// Serializes as a string.
/// </summary>
internal readonly record struct SemVersionSerdeWrap(SemVersion Value)
    : ISerialize, IDeserialize<SemVersion>, ISerializeWrap<SemVersion, SemVersionSerdeWrap>
{
    public static SemVersionSerdeWrap Create(SemVersion value) => new(value);

    public static SemVersion Deserialize<D>(ref D deserializer) where D : IDeserializer
    {
        var str = StringWrap.Deserialize(ref deserializer);
        if (SemVersion.TryParse(str, SemVersionStyles.Strict, out var version))
        {
            return version;
        }
        throw new InvalidDeserializeValueException($"Version string '{str}' is not a valid SemVersion.");
    }

    public void Serialize(ISerializer serializer)
    {
        serializer.SerializeString(Value.ToString());
    }
}