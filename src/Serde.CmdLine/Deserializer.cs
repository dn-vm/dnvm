using System;

namespace Serde.CmdLine;

internal sealed partial class Deserializer(string[] args) : IDeserializer
{
    private int _argIndex = 0;
    private int _paramIndex = 0;

    int IDeserializeType.TryReadIndex(ISerdeInfo serdeInfo, out string? errorName)
    {
        if (_argIndex == args.Length)
        {
            errorName = null;
            return IDeserializeType.EndOfType;
        }
        var arg = args[_argIndex];

        for (int fieldIndex = 0; fieldIndex < serdeInfo.FieldCount; fieldIndex++)
        {
            var attrs = serdeInfo.GetFieldAttributes(fieldIndex);
            foreach (var attr in attrs)
            {
                if (arg.StartsWith('-') &&
                    attr is { AttributeType: { Name: nameof(CommandOptionAttribute) },
                              ConstructorArguments: [ { Value: string flagNames } ] })
                {
                    var flagNamesArray = flagNames.Split('|');
                    foreach (var flag in flagNamesArray)
                    {
                        if (arg == flag)
                        {
                            _argIndex++;
                            errorName = null;
                            return fieldIndex;
                        }
                    }
                }
                else if (!arg.StartsWith('-') &&
                         attr is { AttributeType: { Name: nameof(CommandAttribute) }})
                {
                    errorName = null;
                    return fieldIndex;
                }
                else if (!arg.StartsWith('-') &&
                         attr is { AttributeType: { Name: nameof(CommandParameterAttribute) },
                                   ConstructorArguments: [ { Value: int paramIndex }, _ ] } &&
                         _paramIndex == paramIndex)
                {
                    _paramIndex++;
                    errorName = null;
                    return fieldIndex;
                }
            }
        }
        throw new ArgumentSyntaxException($"Unexpected argument: '{arg}'");
    }

    public bool ReadBool()
    {
        // Flags are a little tricky. They can be specified as --flag or '--flag true' or '--flag false'.
        // There's no way to know for sure whether the current argument is a flag or a value, so we'll
        // try to parse it as a bool. If it fails, we'll assume it's a flag and return true.
        if (_argIndex == args.Length || !bool.TryParse(args[_argIndex], out bool value))
        {
            return true;
        }
        _argIndex++;
        return value;
    }

    public string ReadString() => args[_argIndex++];

    public T ReadNullableRef<T>(IDeserializeVisitor<T> v)
    {
        // Treat all nullable values as just being optional. Since we got here we must have a value
        // in hand.
        return v.VisitNotNull(this);
    }

    public T ReadAny<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    public char ReadChar() => throw new NotImplementedException();

    public byte ReadByte() => throw new NotImplementedException();

    public ushort ReadU16() => throw new NotImplementedException();

    public uint ReadU32() => throw new NotImplementedException();

    public ulong ReadU64() => throw new NotImplementedException();

    public sbyte ReadSByte() => throw new NotImplementedException();

    public short ReadI16() => throw new NotImplementedException();

    public int ReadI32() => throw new NotImplementedException();

    public long ReadI64() => throw new NotImplementedException();

    public float ReadFloat() => throw new NotImplementedException();

    public double ReadDouble() => throw new NotImplementedException();

    public decimal ReadDecimal() => throw new NotImplementedException();

    public IDeserializeCollection ReadCollection(ISerdeInfo typeInfo)
    {
        throw new NotImplementedException();
    }

    public IDeserializeType ReadType(ISerdeInfo typeInfo) => this;

    public void Dispose() { }
}