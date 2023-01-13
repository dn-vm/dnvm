
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class Assets
{
    /// <summary>
    /// Arbitrary string that the FakeSdk `dotnet` file is guaranteed to contain. Check
    /// for its existence to confirm that the archive was expanded correctly.
    /// </summary>
    public const string ArchiveToken = "2c192c853403aa1725f8f99bbe72fe691226fa28";

    private static byte[]? s_sdkArchive = null;
    private static readonly object s_sdkArchiveLock = new();

    public static Stream GetOrMakeFakeSdkArchive()
    {
        if (s_sdkArchive is null)
        {
            lock (s_sdkArchiveLock)
            {
                var archiveName = $"{FakeSdkNameAndVersion}.{Utilities.ZipSuffix}";
                var archivePath = Path.Combine(ArtifactsTestDir.FullName, archiveName);
                try
                {
                    var srcDir = Path.Combine(AssetsDir, FakeSdkNameAndVersion);
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "tar",
                            Arguments = $"-cvzf {archivePath} .",
                            WorkingDirectory = srcDir
                        })!.WaitForExit();
                    }
                    else
                    {
                        ZipFile.CreateFromDirectory(srcDir, archivePath);
                    }
                }
                catch { }
                s_sdkArchive = File.ReadAllBytes(archivePath);
            }
        }
        return new MemoryStream(s_sdkArchive);
    }

    public static FileStream MakeFakeDnvmArchive()
    {
        var archiveName = $"dnvm.{Utilities.ZipSuffix}";
        var archivePath = Path.Combine(ArtifactsTmpDir.FullName, archiveName);
        File.Delete(archivePath);

        try
        {
            // rather than use an actual copy of dnvm, we'll use an executable bash/powershell script
            const string outputString = "Hello from dnvm test. This output must contain the string << usage: >>";
            using var tmpDir = TestUtils.CreateTempDirectory();
            var dnvmScriptPath = Path.Combine(tmpDir.Path, "dnvm");
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                File.WriteAllText(dnvmScriptPath, $$"""
#!/bin/bash
echo '{{outputString}}'
""");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-cvzf {archivePath} dnvm",
                    WorkingDirectory = tmpDir.Path
                })!.WaitForExit();
            }
            else
            {
                var helloCs = Path.Combine(tmpDir.Path, "hello.cs");
                File.WriteAllText(helloCs, $$"""
using System;
class Program {
    public static void Main() {
        Console.WriteLine("{{outputString}}");
    }
}
""");
                Process.Start(new ProcessStartInfo {
                    FileName = "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe",
                    Arguments = "-out:dnvm.exe hello.cs",
                    WorkingDirectory = tmpDir.Path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!.WaitForExit();
                File.Delete(helloCs);
                ZipFile.CreateFromDirectory(tmpDir.Path, archivePath);
            }
        }
        catch { }
        return File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private const string FakeSdkNameAndVersion = "fakesdk-42.42.42";
}