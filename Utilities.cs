using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Dnvm;

static class Utilities
{
    public static string ProcessPath = Environment.ProcessPath!;

    public static string ExeName = "dnvm" + (Program.Rid.Os == Os.win
        ? ".exe"
        : "");

    internal enum Os
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

    internal record struct Rid(Os Os, Arch Arch)
    {
        public override string ToString()
        {
            string os = Os switch
            {
                Os.win => "win",
                Os.linux => "linux",
                Os.linux_musl => "linux_musl",
                Os.osx => "osx",
                _ => throw new NotSupportedException("Unsupported OS")
            };
            string arch = Arch switch
            {
                Arch.x64 => "x64",
                _ => throw new NotSupportedException("Unsupported architecture")
            };
            return $"{os}-{arch}";
        }

        internal static Rid GetRid()
        {
            return new Rid
            {
                Os = 0 switch
                {
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => Os.osx,
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => Os.win,
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.RuntimeIdentifier.Contains("musl") => Os.linux_musl,
                    _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => Os.linux,
                    _ => throw new NotSupportedException("Could not determine OS")
                },
                Arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? Arch.x64 : throw new NotSupportedException($"Unsupported architecture {RuntimeInformation.ProcessArchitecture}")
            };
        }
    }
}