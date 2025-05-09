using System;
using Spectre.Console;

namespace Dnvm;

public enum LogLevel
{
    Error = 1,
    Warn,
    Normal,
    Info,
}

public sealed record Logger(IAnsiConsole Console)
{
    // Mutable for now, should be immutable once the command line parser supports global options
    public LogLevel LogLevel = LogLevel.Normal;

    public void Error(string msg)
    {
        if (LogLevel >= LogLevel.Error)
        {
            Console.MarkupLineInterpolated($"[red]Error: {msg}[/]");
        }
    }

    public void Info(string msg)
    {
        if (LogLevel >= LogLevel.Info)
        {
            Console.WriteLine($"Info({DateTime.UtcNow.TimeOfDay}): {msg}");
        }
    }

    public void Warn(string msg)
    {
        if (LogLevel >= LogLevel.Warn)
        {
            Console.MarkupLineInterpolated($"[yellow]Warning: {msg}[/]");
        }
    }

    public void Log()
    {
        if (LogLevel >= LogLevel.Normal)
        {
            Console.WriteLine();
        }
    }

    public void Log(string msg)
    {
        if (LogLevel >= LogLevel.Normal)
        {
            Console.WriteLine(msg);
        }
    }
}