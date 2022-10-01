
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

sealed class Assets
{
    /// <summary>
    /// Arbitrary string that the FakeSdk `dotnet` file is guaranteed to contain. Check
    /// for its existence to confirm that the archive was expanded correctly.
    /// </summary>
    public const string ArchiveToken = "2c192c853403aa1725f8f99bbe72fe691226fa28";

    public static FileStream GetOrMakeFakeSdkArchive()
    {
        var archiveName = $"{FakeSdkNameAndVersion}.{Utilities.ZipSuffix}";
        var archivePath = Path.Combine(ArtifactsTestDir.FullName, archiveName);
        try
        {
            return File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch { }

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
        return File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private const string FakeSdkNameAndVersion = "fakesdk-42.42.42";
}