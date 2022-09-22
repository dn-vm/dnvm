
using System;

namespace Dnvm;


internal sealed class Logger : ILogger
{
    // Mutable for now, should be immutable once the command line parser supports global options
    public LogLevel LogLevel { private get; set; } = LogLevel.Normal;

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

    public static Logger Default { get; } = new Logger();
}