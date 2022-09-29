using System.Runtime.CompilerServices;
using Xunit;

namespace Dnvm.Test;

public class InstallTests
{
    private static DirectoryInfo ThisDir([CallerFilePath]string path = "") => Directory.GetParent(path)!;

    private static readonly DirectoryInfo TestDir = Directory.CreateDirectory(
        Path.Combine(ThisDir().Parent!.FullName, "artifacts/test"));

    [Fact]
    public Task Install()
    {
        using var testDir = new TempDirectory(Path.Combine(TestDir.FullName, "FakeSdk"));
        Directory.GetFiles(Path.Combine(ThisDir().FullName, "assets/FakeSdk-unix"), )

        var server = new MockServer();
        var options = new Command.InstallOptions()
        {
            TargetUrl = $"http://localhost:{server.Port}/"
        };
        var logger = new Logger();
        var task = new Install(logger, options).Handle();
        return task;
    }
}