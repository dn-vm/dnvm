
using System;
using Serde;

namespace Dnvm;

public sealed class ScalarDeserializer(string s) : IDeserializer
{
    public bool ReadBool()
        => bool.Parse(s);

    public byte ReadByte()
        => byte.Parse(s);

    public char ReadChar()
        => char.Parse(s);

    public decimal ReadDecimal()
        => decimal.Parse(s);

    public double ReadDouble() => double.Parse(s);

    public float ReadFloat() => float.Parse(s);

    public short ReadI16() => short.Parse(s);

    public int ReadI32() => int.Parse(s);

    public long ReadI64() => long.Parse(s);

    public sbyte ReadSByte() => sbyte.Parse(s);

    public string ReadString() => s;

    public ushort ReadU16() => ushort.Parse(s);

    public uint ReadU32() => uint.Parse(s);

    public ulong ReadU64() => ulong.Parse(s);

    void IDisposable.Dispose() { }

    T IDeserializer.ReadAny<T>(IDeserializeVisitor<T> v) => v.VisitString(ReadString());

    IDeserializeCollection IDeserializer.ReadCollection(ISerdeInfo typeInfo)
        => throw new DeserializeException("Found nullable ref, expected scalar");

    T IDeserializer.ReadNullableRef<T, D>(D deserialize)
    {
        return deserialize.Deserialize(this);
    }

    IDeserializeType IDeserializer.ReadType(ISerdeInfo typeInfo)
        => throw new DeserializeException("Found nullable ref, expected scalar");
}