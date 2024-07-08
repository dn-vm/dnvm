using Spectre.Console;

namespace Serde.CmdLine;

public static class CmdLine
{
    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// No errors are handled, so exceptions will be thrown if the arguments are invalid.
    /// </summary>
    public static T ParseRaw<T>(string[] args, bool throwOnHelpRequested = true)
        where T : IDeserialize<T>
    {
        var deserializer = new Deserializer(args);
        var cmd =  T.Deserialize(deserializer);
        if (throwOnHelpRequested && deserializer.HelpRequested)
        {
            throw new HelpRequestedException(deserializer.HelpText);
        }
        return cmd;
    }

    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// If an error occurs, the error message will be printed to the console, followed by the generated help text.
    /// </summary>
    public static bool TryParse<T>(string[] args, IAnsiConsole console, out T cmd)
        where T : IDeserialize<T>
    {
        return TryParse(args, console, CliParseOptions.Default, out cmd);
    }

    /// <summary>
    /// Try to parse the command line arguments directly into a command object.
    /// Returns true if the command was successfully parsed, false otherwise. If help is
    /// requested and <see cref="CliParseOptions.ThrowOnHelpRequested"/> is true, the command is not
    /// parsed and false is returned.
    /// </summary>
    public static bool TryParse<T>(string[] args, IAnsiConsole console, CliParseOptions options, out T cmd)
        where T : IDeserialize<T>
    {
        var deserializer = new Deserializer(args);
        try
        {
            cmd = T.Deserialize(deserializer);
            if (options.ThrowOnHelpRequested && deserializer.HelpRequested)
            {
                console.Write(deserializer.HelpText);
            }
            return true;
        }
        catch (InvalidDeserializeValueException e) when (options.HandleErrors)
        {
            console.WriteLine("error: " + e.Message);
            console.Write(deserializer.HelpText);
        }
        cmd = default!;
        return false;
    }
}

public sealed record CliParseOptions
{
    public static readonly CliParseOptions Default = new();

    /// <summary>
    /// If true, exceptions will be caught and handled by printing the error message to the console,
    /// followed by the generated help text.
    /// </summary>
    public bool HandleErrors { get; init; } = true;

    /// <summary>
    /// If true, the "-h" and "--help" flags will be parsed and ignored, and help text will be
    /// automatically generated. If <see cref="ThrowOnHelpRequested"/> is true, the help text
    /// will be passed into the <see cref="HelpRequestedException"/>. If false, and a console
    /// output handle is passed to parsing, the help text will be printed to the console.
    /// </summary>
    public bool HandleHelp { get; init; } = true;

    /// <summary>
    /// If true, a <see cref="HelpRequestedException"/> will be thrown when "-h" or "--help" is
    /// passed as an argument.
    /// </summary>
    public bool ThrowOnHelpRequested { get; init; } = true;
}