using System.Diagnostics;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class UpdateTests
{
    private readonly Logger _logger;

    public UpdateTests(ITestOutputHelper output)
    {
        var wrapper = new OutputWrapper(output);
        _logger = new Logger(wrapper, wrapper);
    }

    [Fact]
    public async Task SelfUpdate()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);

        await using var mockServer = new MockServer();
        var proc = Process.Start(new ProcessStartInfo() {
            FileName = dnvmTmpPath,
            Arguments = $"update --self -v --feed-url {mockServer.PrefixString}releases.json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        proc!.WaitForExit();
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        Assert.Equal("", error);
        Assert.Equal(0, proc.ExitCode);
        proc = Process.Start(new ProcessStartInfo {
            FileName = dnvmTmpPath,
            RedirectStandardOutput = true
        });
        proc!.WaitForExit();
        Assert.Contains("Hello from dnvm test", proc.StandardOutput.ReadToEnd());
        Assert.Equal(0, proc.ExitCode);
    }

    [Fact]
    public async Task ValidateBinary()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);
        Assert.True(await Update.ValidateBinary(_logger, dnvmTmpPath));
    }
}