namespace Serde.CmdLine;

internal sealed partial class Deserializer : ITypeDeserializer
{
    int? ITypeDeserializer.SizeOpt => null;

    T ITypeDeserializer.ReadValue<T>(ISerdeInfo info, int index, IDeserialize<T> deserialize) => deserialize.Deserialize(this);

    bool ITypeDeserializer.ReadBool(ISerdeInfo info, int index) => ReadBool();

    byte ITypeDeserializer.ReadU8(ISerdeInfo info, int index) => ReadU8();

    char ITypeDeserializer.ReadChar(ISerdeInfo info, int index) => ReadChar();

    decimal ITypeDeserializer.ReadDecimal(ISerdeInfo info, int index) => ReadDecimal();

    double ITypeDeserializer.ReadF64(ISerdeInfo info, int index) => ReadF64();

    float ITypeDeserializer.ReadF32(ISerdeInfo info, int index) => ReadF32();

    short ITypeDeserializer.ReadI16(ISerdeInfo info, int index) => ReadI16();

    int ITypeDeserializer.ReadI32(ISerdeInfo info, int index) => ReadI32();

    long ITypeDeserializer.ReadI64(ISerdeInfo info, int index) => ReadI64();

    sbyte ITypeDeserializer.ReadI8(ISerdeInfo info, int index) => ReadI8();

    string ITypeDeserializer.ReadString(ISerdeInfo info, int index) => ReadString();

    ushort ITypeDeserializer.ReadU16(ISerdeInfo info, int index) => ReadU16();

    uint ITypeDeserializer.ReadU32(ISerdeInfo info, int index) => ReadU32();

    ulong ITypeDeserializer.ReadU64(ISerdeInfo info, int index) => ReadU64();

    void ITypeDeserializer.SkipValue(ISerdeInfo info, int index) => _argIndex++;
}