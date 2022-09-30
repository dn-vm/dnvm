
using System.Runtime.CompilerServices;

namespace Dnvm.Test;

static class TestUtils
{
    private static DirectoryInfo ThisDir([CallerFilePath]string path = "") => Directory.GetParent(path)!;

    public static readonly string AssetsDir = Path.Combine(ThisDir().FullName, "assets");

    public static readonly DirectoryInfo ArtifactsTestDir = Directory.CreateDirectory(
        Path.Combine(ThisDir().Parent!.FullName, "artifacts/test"));

    public static readonly DirectoryInfo ArtifactsTmpDir = Directory.CreateDirectory(
        Path.Combine(ArtifactsTestDir.FullName, "tmp"));

    public static TempDirectory CreateTempDirectory() => TempDirectory.CreateSubDirectory(ArtifactsTmpDir.FullName);
}