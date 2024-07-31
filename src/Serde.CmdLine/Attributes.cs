
using System;

namespace Serde.CmdLine;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CommandOptionAttribute(string flagNames) : Attribute
{
    public string FlagNames { get; } = flagNames;

    public string? Description { get; init; } = null;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CommandParameterAttribute(int ordinal, string name) : Attribute
{
    public int Ordinal { get; } = ordinal;

    public string Name { get; } = name;

    public string? Description { get; init; } = null;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string? Description { get; init; } = null;
}