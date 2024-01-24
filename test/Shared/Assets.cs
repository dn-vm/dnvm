
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Semver;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class Assets
{
    /// <summary>
    /// Arbitrary string that the FakeSdk `dotnet` file is guaranteed to contain. Check
    /// for its existence to confirm that the archive was expanded correctly.
    /// </summary>
    public const string ArchiveToken = "2c192c853403aa1725f8f99bbe72fe691226fa28";

    private static readonly ConcurrentDictionary<SemVersion, byte[]> _archiveCache = new();

    public static Stream GetSdkArchive(
        SemVersion sdkVersion,
        SemVersion runtimeVersion,
        SemVersion aspnetVersion)
    {
        if (_archiveCache.TryGetValue(runtimeVersion, out var archive))
        {
            return new MemoryStream(archive);
        }

        using var tempDir = TestUtils.CreateTempDirectory();
        var exePath = Path.Combine(tempDir.Path, Utilities.DotnetExeName);
        MakeFakeExe(exePath, ArchiveToken);

        File.WriteAllText(Path.Combine(tempDir.Path, "test.txt"), "test text");
        _ = Directory.CreateDirectory(Path.Combine(tempDir.Path, "sdk", sdkVersion.ToString()));
        _ = Directory.CreateDirectory(Path.Combine(tempDir.Path, "shared", "Microsoft.NETCore.App", runtimeVersion.ToString()));
        _ = Directory.CreateDirectory(Path.Combine(tempDir.Path, "host", "fxr", runtimeVersion.ToString()));
        _ = Directory.CreateDirectory(Path.Combine(tempDir.Path, "packs", $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}", runtimeVersion.ToString()));
        _ = Directory.CreateDirectory(Path.Combine(tempDir.Path, "shared", "Microsoft.AspNetCore.App", aspnetVersion.ToString()));
        _ = Directory.CreateDirectory(Path.Combine(tempDir.Path, "templates", aspnetVersion.ToString()));

        using var zipDir = TestUtils.CreateTempDirectory();
        var archivePath = MakeZipOrTarball(tempDir.Path, Path.Combine(zipDir.Path, "dotnet"));
        archive = File.ReadAllBytes(archivePath);
        File.Delete(archivePath);
        return new MemoryStream(_archiveCache.GetOrAdd(runtimeVersion, archive));
    }

    public static void MakeFakeExe(string destPath, string outputString)
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Unix:
            {
                // On Unix we can use a shell script, which looks exactly like an exe
                File.WriteAllText(destPath, $$"""
#!/bin/bash
echo '{{outputString}}'
""");
                break;
            }
            case PlatformID.Win32NT:
            {
                // On Windows we have to make a fake exe, since shell scripts can't have
                // the '.exe' extension
                var helloCs = Path.GetTempFileName();
                File.WriteAllText(helloCs, $$"""
using System;
class Program {
    public static void Main() {
        Console.WriteLine("{{outputString}}");
    }
}
""");
                var proc = Process.Start(new ProcessStartInfo {
                    FileName = "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe",
                    Arguments = $"-out:\"{destPath}\" \"{helloCs}\"",
                    WorkingDirectory = Path.GetDirectoryName(destPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!;
                var output = proc.StandardOutput.ReadToEnd();
                var error = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                File.Delete(helloCs);
                break;
            }
            case var p:
                throw new InvalidOperationException("Unsupported platform: " + p);
        }
    }

    public static string MakeZipOrTarball(string srcDir, string destPathWithoutSuffix)
    {
        var destPath = destPathWithoutSuffix + Utilities.ZipSuffix;
        File.Delete(destPath);
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Unix:
                Process.Start(new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-cvzf {destPath} .",
                    WorkingDirectory = srcDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!.WaitForExit();
                break;
            case PlatformID.Win32NT:
                ZipFile.CreateFromDirectory(srcDir, destPath);
                break;
            case var p:
                throw new InvalidOperationException("Unsupported platform: " + p);
        }
        return destPath;
    }

    public static FileStream MakeFakeDnvmArchive()
    {
        // rather than use an actual copy of dnvm, we'll use an executable bash/powershell script
        const string outputString = "Hello from dnvm test. This output must contain the string << usage: >>";
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmPath = Path.Combine(tmpDir.Path, Utilities.DnvmExeName);
        MakeFakeExe(dnvmPath, outputString);
        var archivePath = MakeZipOrTarball(tmpDir.Path, Path.Combine(ArtifactsTmpDir.FullName, "dnvm"));
        return File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}