
using System;
using System.Collections.Generic;
using System.Reflection;
using Serde;

namespace Serde.CmdLine;

public sealed record UnionSerdeInfo(
    string Name,
    IList<CustomAttributeData> Attributes,
    IEnumerable<ISerdeInfo> CaseInfos) : IUnionSerdeInfo
{
    int ISerdeInfo.FieldCount => 0;

    public IList<CustomAttributeData> GetFieldAttributes(int index) => throw GetOOR(index);

    public ReadOnlySpan<byte> GetFieldName(int index) => throw GetOOR(index);

    public string GetFieldStringName(int index) => throw GetOOR(index);

    public int TryGetIndex(ReadOnlySpan<byte> name) => IDeserializeType.IndexNotFound;

    ISerdeInfo ISerdeInfo.GetFieldInfo(int index) => throw GetOOR(index);

    private IndexOutOfRangeException GetOOR(int index)
    {
        return new IndexOutOfRangeException($"Type {Name} has no fields, but tried to access index {index}");
    }
}