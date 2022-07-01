using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Dnvm;

static class Utilities
{
    public static string? GetOsName()
    {
        string? osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ?
                RuntimeInformation.RuntimeIdentifier.Contains("musl") ? "linux-musl"
                : "linux"
            : null;

        return osName;
    }

    public static string ProcessPath = Environment.ProcessPath!;

    public static string ExeName = "dnvm" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ".exe"
        : "");
}