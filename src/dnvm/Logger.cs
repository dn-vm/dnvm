using Spectre.Console;

namespace Dnvm;

public enum LogLevel
{
    Error = 1,
    Warn,
    Normal,
    Info,
}

public sealed class Logger
{
    private readonly IAnsiConsole _console;

    public Logger(IAnsiConsole console)
    {
        _console = console;
    }

    // Mutable for now, should be immutable once the command line parser supports global options
    public LogLevel LogLevel = LogLevel.Normal;

    public void Error(string msg)
    {
        if (LogLevel >= LogLevel.Error)
        {
            _console.WriteLine("Error: " + msg);
        }
    }

    public void Info(string msg)
    {
        if (LogLevel >= LogLevel.Info)
        {
            _console.WriteLine("Log: " + msg);
        }
    }

    public void Warn(string msg)
    {
        if (LogLevel >= LogLevel.Warn)
        {
            _console.WriteLine("Warning: " + msg);
        }
    }

    public void Log(string msg)
    {
        if (LogLevel >= LogLevel.Normal)
        {
            _console.WriteLine(msg);
        }
    }
}