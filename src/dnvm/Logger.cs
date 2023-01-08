
using System;

namespace Dnvm;

public enum LogLevel
{
    Normal = 1,
    Info = 2
}

public sealed class Logger
{
    // Mutable for now, should be immutable once the command line parser supports global options
    public LogLevel LogLevel = LogLevel.Normal;

    public void Error(string msg)
    {
        Console.Error.WriteLine("Error: " + msg);
    }

    public void Info(string msg)
    {
        if (LogLevel >= LogLevel.Info)
        {
            Console.WriteLine("Log: " + msg);
        }
    }

    public void Log(string msg)
    {
        Console.WriteLine(msg);
    }
}