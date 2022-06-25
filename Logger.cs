
using System;

namespace Dnvm;

enum LogLevel
{
    Normal,
    Info
}

internal sealed class Logger
{
    // Mutable for now, should be immutable once the command line parser supports global options
    public LogLevel LogLevel = LogLevel.Normal;

    public void Error(string msg)
    {
        Console.Error.WriteLine("Error: " + msg);
    }

    public void Info(string msg)
    {
        Console.WriteLine("Log: " + msg);
    }
}