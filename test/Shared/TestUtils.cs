
using System.Runtime.CompilerServices;

namespace Dnvm.Test;

public static class TestUtils
{
    private static DirectoryInfo ThisDir([CallerFilePath]string path = "") => Directory.GetParent(path)!;

    public static readonly string AssetsDir = Path.Combine(ThisDir().FullName, "assets");

    public static readonly string ArtifactsDir = Path.Combine(ThisDir().Parent!.Parent!.FullName, "artifacts");

    public static readonly DirectoryInfo ArtifactsTestDir = Directory.CreateDirectory(
        Path.Combine(ArtifactsDir, "test"));

    public static readonly DirectoryInfo ArtifactsTmpDir = Directory.CreateDirectory(
        Path.Combine(ArtifactsTestDir.FullName, "tmp"));

    public static TempDirectory CreateTempDirectory() => TempDirectory.CreateSubDirectory(ArtifactsTmpDir.FullName);
}