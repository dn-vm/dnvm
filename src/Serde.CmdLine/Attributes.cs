
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

    /// <summary>
    /// Short summary of the command.
    /// </summary>
    public string? Summary { get; init; } = null;

    /// <summary>
    /// Detailed description of the command.
    /// </summary>
    public string? Description { get; init; } = null;
}

/// <summary>
/// Represents one of a group of commands, also known as a discriminated union.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CommandGroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}