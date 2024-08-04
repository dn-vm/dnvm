using System;

namespace Serde.CmdLine;

internal sealed class Deserializer(string[] args) : IDeserializer, IDeserializeType
{
    private int _argIndex = 0;
    private int _paramIndex = 0;

    IDeserializeType IDeserializer.DeserializeType(ISerdeInfo typeInfo)
    {
        return this;
    }

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

    V IDeserializeType.ReadValue<V, D>(int index)
    {
        return D.Deserialize(this);
    }

    T IDeserializer.DeserializeBool<T>(IDeserializeVisitor<T> v)
    {
        // Flags are a little tricky. They can be specified as --flag or '--flag true' or '--flag false'.
        // There's no way to know for sure whether the current argument is a flag or a value, so we'll
        // try to parse it as a bool. If it fails, we'll assume it's a flag and return true.
        if (_argIndex == args.Length || !bool.TryParse(args[_argIndex], out bool value))
        {
            return v.VisitBool(true);
        }
        _argIndex++;
        return v.VisitBool(value);
    }

    T IDeserializer.DeserializeString<T>(IDeserializeVisitor<T> v) => v.VisitString(args[_argIndex++]);

    T IDeserializer.DeserializeNullableRef<T>(IDeserializeVisitor<T> v)
    {
        // Treat all nullable values as just being optional. Since we got here we must have a value
        // in hand.
        return v.VisitNotNull(this);
    }

    T IDeserializer.DeserializeAny<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeChar<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeByte<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeU16<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeU32<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeU64<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeSByte<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeI16<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeI32<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeI64<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeFloat<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeDouble<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeDecimal<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    T IDeserializer.DeserializeIdentifier<T>(IDeserializeVisitor<T> v) => throw new NotImplementedException();

    IDeserializeCollection IDeserializer.DeserializeCollection(ISerdeInfo typeInfo) => throw new NotImplementedException();
}