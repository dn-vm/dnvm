using System;
using System.Collections.Generic;
using System.Reflection;

namespace Serde.CmdLine;

internal sealed partial class Deserializer(string[] args, bool handleHelp) : IDeserializer
{
    private int _argIndex = 0;
    private int _paramIndex = 0;
    private bool _throwOnMissing = true;
    private readonly List<ISerdeInfo> _helpInfos = new();

    public IReadOnlyList<ISerdeInfo> HelpInfos => _helpInfos;

    int ITypeDeserializer.TryReadIndex(ISerdeInfo serdeInfo, out string? errorName)
    {
        if (_argIndex == args.Length)
        {
            errorName = null;
            return ITypeDeserializer.EndOfType;
        }

        var arg = args[_argIndex];
        while (handleHelp && arg is "-h" or "--help")
        {
            _argIndex++;
            _helpInfos.Add(serdeInfo);
            if (_argIndex == args.Length)
            {
                errorName = null;
                return ITypeDeserializer.EndOfType;
            }
            arg = args[_argIndex];
        }

        for (int fieldIndex = 0; fieldIndex < serdeInfo.FieldCount; fieldIndex++)
        {
            IList<CustomAttributeData> attrs = serdeInfo.GetFieldAttributes(fieldIndex);
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
                         attr is { AttributeType: { Name: nameof(CommandAttribute) },
                                   ConstructorArguments: [ { Value: string commandName } ] } &&
                         commandName == arg)
                {
                    _argIndex++;
                    errorName = null;
                    return fieldIndex;
                }
                else if (!arg.StartsWith('-') &&
                         attr is { AttributeType: { Name: nameof(CommandGroupAttribute) } })
                {
                    // If the field is a command group, check to see if any of the nested commands match
                    // the argument. If so, mark this field as a match.
#pragma warning disable SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    var fieldInfo = serdeInfo.GetFieldInfo(fieldIndex);
                    if (fieldInfo.Kind == InfoKind.Nullable)
                    {
                        // Unwrap nullable if present
                        fieldInfo = fieldInfo.GetFieldInfo(0);
                    }
#pragma warning restore SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

                    // Save the argIndex and throwOnMissing so we can restore it after checking.
                    var savedIndex = _argIndex;
                    var savedThrowOnMissing = _throwOnMissing;
                    _throwOnMissing = false;

                    var deType = this.ReadType(fieldInfo);
                    int index = deType.TryReadIndex(fieldInfo, out _);
                    _argIndex = savedIndex;
                    _throwOnMissing = savedThrowOnMissing;

                    if (index >= 0)
                    {
                        // We found a match, so we can return the field index.
                        errorName = null;
                        return fieldIndex;
                    }
                    // No match, so we can continue.
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
        if (_throwOnMissing)
        {
            throw new ArgumentSyntaxException($"Unexpected argument: '{arg}'");
        }
        else
        {
            errorName = arg;
            return ITypeDeserializer.IndexNotFound;
        }
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

    public T ReadNullableRef<T>(IDeserialize<T> d)
        where T : class
    {
        // Treat all nullable values as just being optional. Since we got here we must have a value
        // in hand.
        return d.Deserialize(this);
    }

    public char ReadChar() => throw new NotImplementedException();

    public byte ReadU8() => throw new NotImplementedException();

    public ushort ReadU16() => throw new NotImplementedException();

    public uint ReadU32() => throw new NotImplementedException();

    public ulong ReadU64() => throw new NotImplementedException();

    public sbyte ReadI8() => throw new NotImplementedException();

    public short ReadI16() => throw new NotImplementedException();

    public int ReadI32() => throw new NotImplementedException();

    public long ReadI64() => throw new NotImplementedException();

    public float ReadF32() => throw new NotImplementedException();

    public double ReadF64() => throw new NotImplementedException();

    public decimal ReadDecimal() => throw new NotImplementedException();

    public ITypeDeserializer ReadType(ISerdeInfo typeInfo) => this;

    public void Dispose() { }
}