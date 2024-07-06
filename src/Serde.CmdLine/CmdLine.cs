using System;
using System.Linq;
using Spectre.Console;

namespace Serde.CmdLine;

public static class CmdLine
{
    public static T Parse<T>(string[] args)
        where T : IDeserialize<T>
    {
        return T.Deserialize(new Deserializer(args));
    }

    public static void Run<T>(string[] args, IAnsiConsole console, Action<T> action)
        where T : IDeserialize<T>
    {
        try
        {
            var cmd = Parse<T>(args);
            action(cmd);
        }
        catch (HelpRequestedException e)
        {
            console.Write(e.HelpText);
        }
    }
}