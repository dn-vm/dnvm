
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

    public static Task RunWithServer(Func<MockServer, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            await test(mockServer);
        });

    public static Task RunWithServer(Func<MockServer, DnvmEnv, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl);
            await test(mockServer, testOptions.DnvmEnv);
        });

    public static string StripNewlines(this string input) => input.Replace("\r", "").Replace("\n", "");
}