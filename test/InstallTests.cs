using System.Runtime.CompilerServices;
using Xunit;

namespace Dnvm.Test;

public class InstallTests
{
    [Fact]
    public async Task Install()
    {
        using var tempDir = TestUtils.CreateTempDirectory();
        using var server = new MockServer();
        var options = new Command.InstallOptions()
        {
            FeedUrl = $"http://localhost:{server.Port}/",
            InstallPath = tempDir.Path
        };
        var logger = new Logger();
        var task = new Install(logger, options).Handle();
        await task;
        var dotnetFile = Path.Combine(tempDir.Path, "dotnet");
        Assert.True(File.Exists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, File.ReadAllText(dotnetFile));
    }
}