using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;

namespace Serde.CmdLine;

public static class CmdLine
{
    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// No errors are handled, so exceptions will be thrown if the arguments are invalid.
    /// </summary>
    public static T ParseRaw<T>(string[] args)
        where T : IDeserialize<T>
    {
        var deserializer = new Deserializer(args);
        var cmd =  T.Deserialize(deserializer);
        return cmd;
    }

    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// If an error occurs, the error message will be printed to the console, followed by the generated help text
    /// for the top-level command.
    /// </summary>
    public static bool TryParse<T>(string[] args, IAnsiConsole console, out T cmd)
        where T : IDeserialize<T>
    {
        try
        {
            cmd = ParseRaw<T>(args);
            return true;
        }
        catch (ArgumentSyntaxException ex)
        {
            console.WriteLine("error: " + ex.Message);
            console.WriteLine(GetHelpText(SerdeInfoProvider.GetInfo<T>()));
            cmd = default!;
            return false;
        }
    }

    public static string GetHelpText(ISerdeInfo serdeInfo)
    {
        var args = new List<(string Name, string? Description)>();
        var options = new List<(string[] Patterns, string? Name)>();
        string? commandsName = null;
        var commands = new List<(string Name, string? Description)>();
        for (int fieldIndex = 0; fieldIndex < serdeInfo.FieldCount; fieldIndex++)
        {
            var attrs = serdeInfo.GetFieldAttributes(fieldIndex);
            foreach (var attr in attrs)
            {
                if (attr is { AttributeType: { Name: nameof(CommandOptionAttribute) },
                              ConstructorArguments: [ { Value: string flagNames } ] })
                {
                    // Consider nullable boolean fields as flag options.
#pragma warning disable SerdeExperimentalFieldInfo
                    var optionName = serdeInfo.GetFieldInfo(fieldIndex).Name == "bool?"
#pragma warning restore SerdeExperimentalFieldInfo
                        ? null
                        : $"<{serdeInfo.GetFieldStringName(fieldIndex)}>";
                    options.Add((flagNames.Split('|'), optionName));
                }
                else if (attr is { AttributeType: { Name: nameof(CommandParameterAttribute) },
                               ConstructorArguments: [ { Value: int paramIndex }, { Value: string paramName } ],
                               NamedArguments: var namedArgs })
                {
                    string? desc = null;
                    if (namedArgs is [ { MemberName: nameof(CommandParameterAttribute.Description),
                                         TypedValue: { Value: string attrDesc } } ])
                    {
                        desc = attrDesc;
                    }
                    args.Add(($"<{paramName}>", desc));
                }
                else if (attr is { AttributeType: { Name: nameof(CommandAttribute) },
                                   ConstructorArguments: [ { Value: string commandName }]
                                 })
                {
                    commandsName ??= commandName;
#pragma warning disable SerdeExperimentalFieldInfo
                    var info = serdeInfo.GetFieldInfo(fieldIndex);
#pragma warning restore SerdeExperimentalFieldInfo
                    // The info should be either a nullable wrapper or a union. If it's a
                    // nullable wrapper, unwrap it first.
                    if (info.Kind != InfoKind.Union)
                    {
#pragma warning disable SerdeExperimentalFieldInfo
                        info = info.GetFieldInfo(0);
#pragma warning restore SerdeExperimentalFieldInfo
                    }
                    var unionInfo = (IUnionSerdeInfo)info;
                    foreach (var unionField in unionInfo.CaseInfos)
                    {
                        var cmdName = unionField.Name;
                        string? desc = null;
                        foreach (var caseAttr in unionField.Attributes)
                        {
                            if (caseAttr is { AttributeType: { Name: nameof(CommandAttribute) },
                                              ConstructorArguments: [ { Value: string caseCmdName } ],
                                              NamedArguments: var namedCaseArgs })
                            {
                                cmdName = caseCmdName;
                                if (namedCaseArgs is [ { MemberName: nameof(CommandAttribute.Description),
                                                         TypedValue: { Value: string caseDesc } } ])
                                {
                                    desc = caseDesc;
                                }
                                break;
                            }
                        }
                        commands.Add((cmdName, desc));
                    }
                }
            }
        }

        const string Indent = "    ";

        var commandsString = commands.Count == 0
            ? ""
            : $"""

Commands:
{Indent + string.Join(Environment.NewLine + Indent,
    commands.Select(c => $"{c.Name}{c.Description?.Prepend("  ") ?? ""}"))}

""";

        var argsString = args.Count > 0
            ? $"""

Arguments:
{Indent + string.Join(Environment.NewLine + Indent,
    args.Select(a => $"{a.Name}{a.Description?.Prepend("  ") ?? ""}"))}

"""
            : "";

        var optionsString = options.Count > 0
            ? $"""

Options:
{Indent + string.Join(Environment.NewLine + Indent,
    options.Select(o => $"{string.Join(", ", o.Patterns)}{o.Name?.Map(n => "  " + n) ?? "" }"))}

"""
            : "";

        var optionsUsageShortString = options.Count > 0
            ? " " + string.Join(" ",
                options.Select(o => $"[{string.Join(" | ", o.Patterns)}{o.Name?.Map(n => " " + n) ?? "" }]"))
            : "";

        var topLevelName = serdeInfo.Name;
        string topLevelDesc = "";
        foreach (var attr in serdeInfo.Attributes)
        {
            if (attr is { AttributeType: { Name: nameof(CommandAttribute) },
                          ConstructorArguments: [ { Value: string name } ],
                          NamedArguments: var namedArgs })
            {
                topLevelName = name;
                if (namedArgs is [ { MemberName: nameof(CommandAttribute.Description),
                                    TypedValue: { Value: string desc } } ])
                {
                    topLevelDesc = Environment.NewLine + desc + Environment.NewLine;
                }
                break;
            }
        }

        var argsShortString = args.Count > 0
            ? " " + string.Join(" ", args.Select(a => a.Name))
            : "";

        return $"""
Usage: {topLevelName}{optionsUsageShortString}{commandsName?.Map(n => $" <{n}>") ?? ""}{argsShortString}
{topLevelDesc}{argsString}{optionsString}{commandsString}
""";
    }


}