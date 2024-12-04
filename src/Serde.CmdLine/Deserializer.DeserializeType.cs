using Serde;

namespace Serde.CmdLine;

internal sealed partial class Deserializer : IDeserializeType
{
    V IDeserializeType.ReadValue<V, D>(int index) => D.Deserialize(this);

    bool IDeserializeType.ReadBool(int index) => ReadBool();

    byte IDeserializeType.ReadByte(int index) => ReadByte();

    char IDeserializeType.ReadChar(int index) => ReadChar();

    decimal IDeserializeType.ReadDecimal(int index) => ReadDecimal();

    double IDeserializeType.ReadDouble(int index) => ReadDouble();

    float IDeserializeType.ReadFloat(int index) => ReadFloat();

    short IDeserializeType.ReadI16(int index) => ReadI16();

    int IDeserializeType.ReadI32(int index) => ReadI32();

    long IDeserializeType.ReadI64(int index) => ReadI64();

    sbyte IDeserializeType.ReadSByte(int index) => ReadSByte();

    string IDeserializeType.ReadString(int index) => ReadString();

    ushort IDeserializeType.ReadU16(int index) => ReadU16();

    uint IDeserializeType.ReadU32(int index) => ReadU32();

    ulong IDeserializeType.ReadU64(int index) => ReadU64();

    void IDeserializeType.SkipValue() => _argIndex++;
}