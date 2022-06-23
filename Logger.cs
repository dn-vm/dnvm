
using System;

namespace Dnvm;

internal sealed class Logger
{
    public void Log(string msg)
    {
        Console.WriteLine("Log: " + msg);
    }
}