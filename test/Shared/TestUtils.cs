
using System.Runtime.CompilerServices;
using Semver;
using Zio;

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

    public static Task RunWithServer(Func<MockServer, TestEnv, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl);
            await test(mockServer, testOptions);
        });

    public static Task RunWithServer(UPath cwd, Func<MockServer, TestEnv, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl, cwd);
            await test(mockServer, testOptions);
        });

    public static string RemoveWhitespace(this string input) => input.Replace("\r", "").Replace("\n", "").Replace(" ", "");

    public static ChannelReleaseIndex.Release CreateRelease(string prefixString, SemVersion universalVersion)
    {
        var rid = Utilities.CurrentRID.ToString();
        var file = new ChannelReleaseIndex.File()
        {
            Name = $"dotnet-sdk-{rid}{Utilities.ZipSuffix}",
            Rid = rid,
            Url = $"{prefixString}sdk/{universalVersion}/dotnet-sdk-{rid}{Utilities.ZipSuffix}",
            Hash = ""
        };
        var component = new ChannelReleaseIndex.Component
        {
            Version = universalVersion,
            Files = [ file ]
        };
        return new()
        {
            ReleaseVersion = universalVersion,
            Runtime = component,
            Sdk = component,
            Sdks = [component],
            AspNetCore = component,
            WindowsDesktop = component,
        };
    }
}