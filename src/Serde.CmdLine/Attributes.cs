
using System;

namespace Serde.CmdLine;

public sealed class CommandOptionAttribute(string flagNames) : Attribute
{
    public string FlagNames { get; } = flagNames;

    public string? Description { get; init; } = null;
}

public sealed class CommandParameterAttribute(int ordinal, string name) : Attribute
{
    public int Ordinal { get; } = ordinal;

    public string Name { get; } = name;

    public string? Description { get; init; } = null;
}