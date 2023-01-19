
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using StaticCs;

namespace Dnvm;

public static class Utilities
{
    public static readonly string ZipSuffix = Environment.OSVersion.Platform == PlatformID.Win32NT ? "zip" : "tar.gz";

    public static string SeqToString<T>(this IEnumerable<T> e)
    {
        return "[ " + string.Join(", ", e.ToString()) + " ]";
    }

    public static readonly RID CurrentRID = new RID(
        GetCurrentOSPlatform(),
        RuntimeInformation.OSArchitecture,
        RuntimeInformation.RuntimeIdentifier.Contains("musl") ? Libc.Musl : Libc.Default);

    private static OSPlatform GetCurrentOSPlatform()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux :
            throw new NotSupportedException("Current OS is not supported: " + RuntimeInformation.OSDescription);
    }

    public static bool IsSingleFile =>
#pragma warning disable IL3000
        Assembly.GetExecutingAssembly()?.Location == "";
#pragma warning restore IL3000

    public static string ProcessPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot find exe name");

    public static string ExeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ".exe"
        : "";

    public static string ExeName = "dnvm" + ExeSuffix;

    public static async Task<string?> ExtractArchiveToDir(string archivePath, string dirPath)
    {
        Directory.CreateDirectory(dirPath);
        if (Utilities.CurrentRID.OS != OSPlatform.Windows)
        {
            var proc = Process.Start(new ProcessStartInfo() {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{dirPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            await proc!.WaitForExitAsync();
            return proc.ExitCode == 0 ? null : "";
        }
        else
        {
            try
            {
                ZipFile.ExtractToDirectory(archivePath, dirPath, overwriteFiles: true);
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