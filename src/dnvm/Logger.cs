using System.IO;

namespace Dnvm;

public sealed class Logger(TextWriter console)
{
    public bool Enabled { get; set; } = false;

    public void Log()
    {
        if (Enabled)
        {
            console.WriteLine();
        }
    }

    public void Log(string message)
    {
        if (Enabled)
        {
            console.WriteLine(message);
        }
    }
}