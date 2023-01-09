
using System;

namespace Dnvm;

public enum LogLevel
{
    Info = 1,
    Normal,
    Warn,
    Error
}

public sealed class Logger
{
    // Mutable for now, should be immutable once the command line parser supports global options
    public LogLevel LogLevel = LogLevel.Normal;

    public void Error(string msg)
    {
        if (LogLevel >= LogLevel.Error)
        {
            Console.Error.WriteLine("Error: " + msg);
        }
    }

    public void Info(string msg)
    {
        if (LogLevel >= LogLevel.Info)
        {
            Console.WriteLine("Log: " + msg);
        }
    }

    public void Warn(string msg)
    {
        if (LogLevel >= LogLevel.Warn)
        {
            Console.WriteLine("Warning: " + msg);
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