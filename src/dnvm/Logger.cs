
using System;
using System.IO;

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
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public Logger(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    // Mutable for now, should be immutable once the command line parser supports global options
    public LogLevel LogLevel = LogLevel.Normal;

    public void Error(string msg)
    {
        if (LogLevel >= LogLevel.Error)
        {
            _error.WriteLine("Error: " + msg);
        }
    }

    public void Info(string msg)
    {
        if (LogLevel >= LogLevel.Info)
        {
            _output.WriteLine("Log: " + msg);
        }
    }

    public void Warn(string msg)
    {
        if (LogLevel >= LogLevel.Warn)
        {
            _output.WriteLine("Warning: " + msg);
        }
    }

    public void Log(string msg)
    {
        if (LogLevel >= LogLevel.Normal)
        {
            _output.WriteLine(msg);
        }
    }
}