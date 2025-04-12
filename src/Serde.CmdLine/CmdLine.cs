using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using StaticCs;

namespace Serde.CmdLine;

public static class CmdLine
{
    [Closed]
    public abstract record ParsedArgsOrHelpInfos<TArgs>
    {
        private ParsedArgsOrHelpInfos() { }
        public sealed record Parsed(TArgs Args) : ParsedArgsOrHelpInfos<TArgs>;
        public sealed record Help(IReadOnlyList<ISerdeInfo> HelpInfos) : ParsedArgsOrHelpInfos<TArgs>;
    }

    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// No errors are handled, so exceptions will be thrown if the arguments are invalid.
    /// </summary>
    public static ParsedArgsOrHelpInfos<T> ParseRawWithHelp<T>(string[] args)
        where T : IDeserializeProvider<T>
    {
        var deserializer = new Deserializer(args, handleHelp: true);
        try
        {
            var cmd = T.Instance.Deserialize(deserializer);
            if (deserializer.HelpInfos.Count > 0)
            {
                return new ParsedArgsOrHelpInfos<T>.Help(deserializer.HelpInfos);
            }
            else
            {
                return new ParsedArgsOrHelpInfos<T>.Parsed(cmd);
            }
        }
        catch (DeserializeException e)
        {
            // If the deserializer was created and there's at least one help info added, provide a
            // null command and the help infos, ignoring what would otherwise be an error.
            if (deserializer.HelpInfos.Count > 0)
            {
                return new ParsedArgsOrHelpInfos<T>.Help(deserializer.HelpInfos);
            }
            throw new ArgumentSyntaxException(e.Message, e);
        }
    }

    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// No errors are handled, so exceptions will be thrown if the arguments are invalid.
    /// </summary>
    public static T ParseRaw<T>(string[] args) where T : IDeserializeProvider<T>
    {
        var deserializer = new Deserializer(args, handleHelp: false);
        try
        {
            return T.Instance.Deserialize(deserializer);
        }
        catch (DeserializeException e)
        {
            throw new ArgumentSyntaxException(e.Message, e);
        }
    }

    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// If an error occurs, the error message will be printed to the console, followed by the generated help text
    /// for the top-level command.
    /// </summary>
    public static bool TryParse<T>(string[] args, IAnsiConsole console, out T cmd)
        where T : IDeserializeProvider<T>
    {
        try
        {
            var result = ParseRawWithHelp<T>(args);
            switch (result)
            {
                case ParsedArgsOrHelpInfos<T>.Parsed(var value):
                    cmd = value;
                    return true;
                case ParsedArgsOrHelpInfos<T>.Help(var helpInfos):
                    var rootInfo = SerdeInfoProvider.GetDeserializeInfo<T>();
                    var lastInfo = helpInfos.Last();
                    console.WriteLine(CmdLine.GetHelpText(rootInfo, lastInfo, includeHelp: true));
                    cmd = default!;
                    return false;
                default:
                    throw new InvalidOperationException();
            }
        }
        catch (ArgumentSyntaxException ex)
        {
            console.WriteLine("error: " + ex.Message);
            console.WriteLine(GetHelpText<T>());
            cmd = default!;
            return false;
        }
    }

    public static string GetHelpText<T>(bool includeHelp = false)
        where T : IDeserializeProvider<T>
    {
        var rootInfo = SerdeInfoProvider.GetDeserializeInfo<T>();
        return GetHelpText(rootInfo, includeHelp);
    }

    public static string GetHelpText(ISerdeInfo rootInfo, bool includeHelp = false) => GetHelpText(rootInfo, rootInfo, includeHelp);

    public static string GetHelpText(ISerdeInfo rootInfo, ISerdeInfo targetInfo, bool includeHelp = false)
    {
        var args = new List<(string Name, string? Description)>();
        var options = new List<(string[] Patterns, string? Name, string? Description)>();
        string? commandsName = null;
        var commands = new List<(string Name, string? Summary, string? Description)>();
        for (int fieldIndex = 0; fieldIndex < targetInfo.FieldCount; fieldIndex++)
        {
            var attrs = targetInfo.GetFieldAttributes(fieldIndex);
            foreach (var attr in attrs)
            {
                if (attr is { AttributeType: { Name: nameof(CommandOptionAttribute) },
                              ConstructorArguments: [ { Value: string flagNames } ],
                              NamedArguments: var namedArgs })
                {
                    // Consider nullable boolean fields as flag options.
#pragma warning disable SerdeExperimentalFieldInfo
                    var optionName = targetInfo.GetFieldInfo(fieldIndex).Name == "bool?"
#pragma warning restore SerdeExperimentalFieldInfo
                        ? null
                        : $"<{targetInfo.GetFieldStringName(fieldIndex)}>";
                    string? desc = null;
                    if (namedArgs is [ { MemberName: nameof(CommandParameterAttribute.Description),
                                         TypedValue: { Value: string attrDesc } } ])
                    {
                        desc = attrDesc;
                    }
                    options.Add((flagNames.Split('|'), optionName, desc));
                }
                else if (attr is { AttributeType: { Name: nameof(CommandParameterAttribute) },
                               ConstructorArguments: [ { Value: int paramIndex }, { Value: string paramName } ],
                               NamedArguments: var namedArgs2 })
                {
                    string? desc = null;
                    if (namedArgs2 is [ { MemberName: nameof(CommandParameterAttribute.Description),
                                         TypedValue: { Value: string attrDesc } } ])
                    {
                        desc = attrDesc;
                    }
                    args.Add(($"<{paramName}>", desc));
                }
                else if (attr is { AttributeType: { Name: nameof(CommandGroupAttribute) },
                                   ConstructorArguments: [ { Value: string commandName }]
                                 })
                {
                    commandsName ??= commandName;
#pragma warning disable SerdeExperimentalFieldInfo
                    var info = targetInfo.GetFieldInfo(fieldIndex);
                    // If the info is a nullable wrapper, unwrap it first.
                    if (info.Kind == InfoKind.Nullable)
                    {
                        info = info.GetFieldInfo(0);
                    }
#pragma warning restore SerdeExperimentalFieldInfo

                    foreach (var caseInfo in ((IUnionSerdeInfo)info).CaseInfos)
                    {
                        AddCommand(commands, caseInfo);
                    }
                }
                else if (attr is { AttributeType: { Name: nameof(CommandAttribute) } })
                {
#pragma warning disable SerdeExperimentalFieldInfo
                    var info = targetInfo.GetFieldInfo(fieldIndex);
#pragma warning restore SerdeExperimentalFieldInfo

                    AddCommand(commands, info);
                }

                static void AddCommand(
                    List<(string Name, string? Summary, string? Description)> commands,
                    ISerdeInfo commandInfo
                )
                {
                    var cmdName = commandInfo.Name;
                    string? summary = null;
                    string? desc = null;
                    foreach (var caseAttr in commandInfo.Attributes)
                    {
                        if (caseAttr is
                            {
                                AttributeType: { Name: nameof(CommandAttribute) },
                                ConstructorArguments: [{ Value: string caseCmdName }],
                                NamedArguments: var namedCaseArgs
                            })
                        {
                            cmdName = caseCmdName;
                            foreach (var namedArg in namedCaseArgs)
                            {
                                if (namedArg is
                                    {
                                        MemberName: nameof(CommandAttribute.Summary),
                                        TypedValue: { Value: string caseSummary }
                                    })
                                {
                                    summary = caseSummary;
                                }
                                if (namedArg is
                                    {
                                        MemberName: nameof(CommandAttribute.Description),
                                        TypedValue: { Value: string caseDesc }
                                    })
                                {
                                    desc = caseDesc.ReplaceLineEndings();
                                }
                            }
                            break;
                        }
                    }
                    commands.Add((cmdName, summary, desc));
                }
            }
        }

        if (includeHelp)
        {
            options.Add((new[] { "-h", "--help" }, null, "Show help information."));
        }

        const string Indent = "    ";

        var commandsString = commands.Count == 0
            ? ""
            : $"""
Commands:
{Indent + string.Join(Environment.NewLine + Indent,
    commands.Select(c => $"{c.Name}{c.Summary?.Prepend("  ") ?? ""}"))}
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
    options.Select(o => $"{string.Join(", ", o.Patterns)}{o.Name?.Map(n => "  " + n) ?? "" }{o.Description?.Prepend("  ") ?? ""}"))}
"""
            : "";

        var optionsUsageShortString = options.Count > 0
            ? " " + string.Join(" ",
                options.Select(o => $"[{string.Join(" | ", o.Patterns)}{o.Name?.Map(n => " " + n) ?? "" }]"))
            : "";

        string topLevelDesc = "";
        foreach (var attr in targetInfo.Attributes)
        {
            if (attr is { AttributeType: { Name: nameof(CommandAttribute) },
                          ConstructorArguments: [ { Value: string name } ],
                          NamedArguments: var namedArgs })
            {
                foreach (var named in namedArgs)
                {
                    if (named is {
                            MemberName: nameof(CommandAttribute.Summary),
                            TypedValue: { Value: string summary } })
                    {
                        topLevelDesc = Environment.NewLine + summary + Environment.NewLine;
                    }
                    if (named is {
                            MemberName: nameof(CommandAttribute.Description),
                            TypedValue: { Value: string desc } })
                    {
                        topLevelDesc = Environment.NewLine + desc + Environment.NewLine;
                    }
                }
                break;
            }
        }

        var parentCommandInfos = GetParentInfos(rootInfo, targetInfo);

        var topLevelName = string.Join(" ", parentCommandInfos.Select(GetCommandName));

        var argsShortString = args.Count > 0
            ? " " + string.Join(" ", args.Select(a => a.Name))
            : "";

        var remainingString = string.Join(Environment.NewLine + Environment.NewLine,
            ((string[])[ argsString, optionsString, commandsString ]).Where(s => !string.IsNullOrWhiteSpace(s)));

        return $"""
usage: {topLevelName}{optionsUsageShortString}{commandsName?.Map(n => $" <{n}>") ?? ""}{argsShortString}
{topLevelDesc}
{remainingString}

""";

        /// Get the chain of ISerdeInfo objects from the root to the target ISerdeInfo.
        static IEnumerable<ISerdeInfo> GetParentInfos(ISerdeInfo rootInfo, ISerdeInfo targetInfo)
        {
            var parentInfos = new List<ISerdeInfo> { rootInfo };
            if (BuildParentInfos(parentInfos, rootInfo, targetInfo))
            {
                return parentInfos;
            }
            throw new InvalidOperationException("Could not find path from root to target.");

            static bool BuildParentInfos(List<ISerdeInfo> parentInfos, ISerdeInfo currentInfo, ISerdeInfo targetInfo)
            {
                // Unwrap nullable types
                if (currentInfo.Kind == InfoKind.Nullable)
                {
#pragma warning disable SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    currentInfo = currentInfo.GetFieldInfo(0);
#pragma warning restore SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                }

                if (currentInfo == targetInfo)
                {
                    return true;
                }
                for (int i = 0; i < currentInfo.FieldCount; i++)
                {
                    var attrs = currentInfo.GetFieldAttributes(i);

                    foreach (var attr in attrs)
                    {
                        if (attr is { AttributeType: { Name: nameof(CommandGroupAttribute) }})
                        {
#pragma warning disable SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                            var nextInfo = currentInfo.GetFieldInfo(i);
#pragma warning restore SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                            // Don't push the group itself
                            if (BuildParentInfos(parentInfos, nextInfo, targetInfo))
                            {
                                return true;
                            }
                        }
                        else if (attr is { AttributeType: { Name: nameof(CommandAttribute)}})
                        {
#pragma warning disable SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                            var nextInfo = currentInfo.GetFieldInfo(i);
#pragma warning restore SerdeExperimentalFieldInfo // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                            parentInfos.Add(nextInfo);
                            if (BuildParentInfos(parentInfos, nextInfo, targetInfo))
                            {
                                return true;
                            }
                            parentInfos.RemoveAt(parentInfos.Count - 1);
                        }
                    }
                }
                return false;
            }
        }
    }

    public static string GetCommandName(ISerdeInfo serdeInfo)
    {
        var name = serdeInfo.Name;
        foreach (var attr in serdeInfo.Attributes)
        {
            if (attr is { AttributeType: { Name: nameof(CommandAttribute) },
                          ConstructorArguments: [ { Value: string commandName } ] })
            {
                name = commandName;
                break;
            }
        }
        return name;
    }

}