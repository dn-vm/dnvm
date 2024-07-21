
using System;
using Serde;

namespace Dnvm;

public struct ScalarDeserializer(string s) : IDeserializer
{
    public T DeserializeAny<T>(IDeserializeVisitor<T> v)
    {
        throw new NotImplementedException();
    }

    public T DeserializeBool<T>(IDeserializeVisitor<T> v)
        => v.VisitBool(bool.Parse(s));

    public T DeserializeByte<T>(IDeserializeVisitor<T> v)
        => v.VisitByte(byte.Parse(s));

    public T DeserializeChar<T>(IDeserializeVisitor<T> v)
        => v.VisitChar(char.Parse(s));

    public T DeserializeDecimal<T>(IDeserializeVisitor<T> v)
        => v.VisitDecimal(decimal.Parse(s));

    public T DeserializeDictionary<T>(IDeserializeVisitor<T> v)
        => throw new InvalidDeserializeValueException("Found dictionary, expected scalar");

    public T DeserializeDouble<T>(IDeserializeVisitor<T> v)
        => v.VisitDouble(double.Parse(s));

    public T DeserializeEnumerable<T>(IDeserializeVisitor<T> v)
        => throw new InvalidDeserializeValueException("Found enumerable, expected scalar");

    public T DeserializeFloat<T>(IDeserializeVisitor<T> v)
        => v.VisitFloat(float.Parse(s));

    public T DeserializeI16<T>(IDeserializeVisitor<T> v)
        => v.VisitI16(short.Parse(s));

    public T DeserializeI32<T>(IDeserializeVisitor<T> v)
        => v.VisitI32(int.Parse(s));

    public T DeserializeI64<T>(IDeserializeVisitor<T> v)
        => v.VisitI64(long.Parse(s));

    public T DeserializeIdentifier<T>(IDeserializeVisitor<T> v)
        => throw new InvalidDeserializeValueException("Found identifier, expected scalar");

    public T DeserializeNullableRef<T>(IDeserializeVisitor<T> v)
        => throw new InvalidDeserializeValueException("Found nullable ref, expected scalar");

    public T DeserializeSByte<T>(IDeserializeVisitor<T> v)
        => v.VisitSByte(sbyte.Parse(s));

    public T DeserializeString<T>(IDeserializeVisitor<T> v)
        => v.VisitString(s);

    public T DeserializeU16<T>(IDeserializeVisitor<T> v)
        => v.VisitU16(ushort.Parse(s));

    public T DeserializeU32<T>(IDeserializeVisitor<T> v)
        => v.VisitU32(uint.Parse(s));

    public T DeserializeU64<T>(IDeserializeVisitor<T> v)
        => v.VisitU64(ulong.Parse(s));

    IDeserializeCollection IDeserializer.DeserializeCollection(ISerdeInfo typeInfo)
        => throw new InvalidDeserializeValueException("Found enumerable, expected scalar");

    IDeserializeType IDeserializer.DeserializeType(ISerdeInfo typeInfo)
        => throw new InvalidDeserializeValueException("Found type, expected scalar");
}