
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using StaticCs;

namespace Dnvm;

/// <summary>
/// Deletes the given directory on disposal.
/// </summary>
public readonly record struct DirectoryResource(
    string Path,
    bool Recursive = true) : IDisposable
{
    public void Dispose()
    {
        Directory.Delete(Path, recursive: Recursive);
    }
}

public static class Utilities
{
    public static readonly string ZipSuffix = Environment.OSVersion.Platform == PlatformID.Win32NT ? ".zip" : ".tar.gz";

    [UnsupportedOSPlatform("windows")]
    public static void ChmodExec(string path)
    {
        var mod = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mod | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

    }

    public static string SeqToString<T>(this IEnumerable<T> e)
    {
        return "[ " + string.Join(", ", e.ToString()) + " ]";
    }

    public static ImmutableArray<U> SelectAsArray<T, U>(this ImmutableArray<T> e, Func<T, U> f)
    {
        var builder = ImmutableArray.CreateBuilder<U>(e.Length);
        foreach (var item in e)
        {
            builder.Add(f(item));
        }
        return builder.MoveToImmutable();
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

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Checks for empty location")]
    public static bool IsSingleFile => Assembly.GetExecutingAssembly()?.Location == "";

    public static string ProcessPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot find exe name");

    public static string ExeSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ".exe"
        : "";

    public static string DnvmExeName = "dnvm" + ExeSuffix;
    public static string DotnetExeName = "dotnet" + ExeSuffix;
    public static string DotnetSymlinkName = "dotnet" + (OperatingSystem.IsWindows()
        ? ".cmd"
        : "");

    public static async Task<string?> ExtractArchiveToDir(string archivePath, string dirPath)
    {
        Directory.CreateDirectory(dirPath);
        if (Utilities.CurrentRID.OS != OSPlatform.Windows)
        {
            var procResult = await ProcUtil.RunWithOutput("tar", $"-xzf \"{archivePath}\" -C \"{dirPath}\"");
            return procResult.ExitCode == 0 ? null : procResult.Error;
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