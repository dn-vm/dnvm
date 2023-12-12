
using System;
using Serde;

namespace Dnvm;

public struct ScalarDeserializer(string s) : IDeserializer
{
    public T DeserializeAny<T, V>(V v) where V : IDeserializeVisitor<T>
    {
        throw new NotImplementedException();
    }

    public T DeserializeBool<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitBool(bool.Parse(s));

    public T DeserializeByte<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitByte(byte.Parse(s));

    public T DeserializeChar<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitChar(char.Parse(s));

    public T DeserializeDecimal<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitDecimal(decimal.Parse(s));

    public T DeserializeDictionary<T, V>(V v) where V : IDeserializeVisitor<T>
        => throw new InvalidDeserializeValueException("Found dictionary, expected scalar");

    public T DeserializeDouble<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitDouble(double.Parse(s));

    public T DeserializeEnumerable<T, V>(V v) where V : IDeserializeVisitor<T>
        => throw new InvalidDeserializeValueException("Found enumerable, expected scalar");

    public T DeserializeFloat<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitFloat(float.Parse(s));

    public T DeserializeI16<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitI16(short.Parse(s));

    public T DeserializeI32<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitI32(int.Parse(s));

    public T DeserializeI64<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitI64(long.Parse(s));

    public T DeserializeIdentifier<T, V>(V v) where V : IDeserializeVisitor<T>
        => throw new InvalidDeserializeValueException("Found identifier, expected scalar");

    public T DeserializeNullableRef<T, V>(V v) where V : IDeserializeVisitor<T>
        => throw new InvalidDeserializeValueException("Found nullable ref, expected scalar");

    public T DeserializeSByte<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitSByte(sbyte.Parse(s));

    public T DeserializeString<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitString(s);

    public T DeserializeType<T, V>(string typeName, ReadOnlySpan<string> fieldNames, V v) where V : IDeserializeVisitor<T>
        => throw new InvalidDeserializeValueException("Found type, expected scalar");

    public T DeserializeU16<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitU16(ushort.Parse(s));

    public T DeserializeU32<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitU32(uint.Parse(s));

    public T DeserializeU64<T, V>(V v) where V : IDeserializeVisitor<T>
        => v.VisitU64(ulong.Parse(s));
}