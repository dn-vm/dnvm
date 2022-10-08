

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using StaticCs;

namespace Dnvm;

public static class Utilities
{
    public static readonly string ZipSuffix = Environment.OSVersion.Platform == PlatformID.Win32NT ? "zip" : "tar.gz";

    public static readonly RID CurrentRID = new RID(
        GetCurrentOSPlatform(),
        RuntimeInformation.ProcessArchitecture,
        RuntimeInformation.RuntimeIdentifier.Contains("musl") ? Libc.Musl : Libc.Default);

    private static OSPlatform GetCurrentOSPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux :
            throw new NotSupportedException("Current OS is not supported: " + RuntimeInformation.OSDescription);
    }

    public static string ProcessPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot find exe name");

    public static string ExeName = "dnvm" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ".exe"
        : "");

    public static async Task<string?> ExtractArchiveToDir(string archivePath, string dirPath)
    {
        Directory.CreateDirectory(dirPath);
        if (Utilities.CurrentRID.OS != OSPlatform.Windows)
        {
            var psi = new ProcessStartInfo()
            {
                FileName = "tar",
                ArgumentList = { "-xzf", $"{archivePath}", "-C", $"{dirPath}" },
            };

            var p = Process.Start(psi);
            if (p is not null)
            {
                await p.WaitForExitAsync();
                return p.ExitCode == 0 ? null : p.StandardError.ReadToEnd();
            }
            return "Could not start process";
        }
        else
        {
            try
            {
                ZipFile.ExtractToDirectory(archivePath, dirPath);
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
        return null;
    }

}

[Closed]
public enum Libc
{
    Default, // Not a real libc, refers to the most common platform libc
    Musl
}

public readonly record struct RID(
    OSPlatform OS,
    Architecture Arch,
    Libc Libc = Libc.Default)
{
    public override string ToString()
    {
        string os =
            OS == OSPlatform.Windows ? "win" :
            OS == OSPlatform.Linux   ? "linux" :
            OS == OSPlatform.OSX ? "osx" :
            throw new NotSupportedException("Unsupported OS: " + OS);

        string arch = Arch switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new NotSupportedException("Unsupported architecture")
        };
        return Libc switch
        {
            Libc.Default => string.Join("-", os, arch),
            Libc.Musl => string.Join('-', os, arch, "musl")
        };
    }
}