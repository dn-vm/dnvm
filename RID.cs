using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace dnvm;

internal enum OS
{
    win,
    linux,
    linux_musl,
    osx
}

internal enum Arch
{
    x64
}

internal record struct RID(OS OS, Arch Arch) : IFormattable
{
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        string os = OS switch
        {
            OS.win => "win",
            OS.linux => "linux",
            OS.linux_musl => "linux_musl",
            OS.osx => "osx",
            _ => throw new NotSupportedException("Unsupported OS")
        };
        string arch = Arch switch
        {
            Arch.x64 => "x64",
            _ => throw new NotSupportedException("Unsupported architecture")
        };
        return $"{os}-{arch}";
    }

    internal static RID GetRid()
    {
        return new RID
        {
            OS = 0 switch
            {
                _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => OS.osx,
                _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => OS.win,
                _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.RuntimeIdentifier.Contains("musl") => OS.linux_musl,
                _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => OS.linux,
                _ => throw new NotSupportedException("Could not determine OS")
            },
            Arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? Arch.x64 : throw new NotSupportedException($"Unsupported architecture {RuntimeInformation.ProcessArchitecture}")
        };
    }
}

