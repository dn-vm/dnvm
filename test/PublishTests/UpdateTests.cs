using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Dnvm.Test;

public class UpdateTests
{
    private static readonly string DnvmExe = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "dnvm_aot/dnvm" + (Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : ""));

    [Fact]
    public void SelfUpdate()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(DnvmExe);

        using var mockServer = new MockServer();
        var proc = Process.Start(new ProcessStartInfo() {
            FileName = dnvmTmpPath,
            Arguments = $"update --self -v --releases-url {mockServer.PrefixString}releases.json",
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
        var dnvmTmpPath = tmpDir.CopyFile(DnvmExe);
        var logger = new Logger();
        var update = new Update(logger, new Command.UpdateOptions());
        Assert.True(await update.ValidateBinary(dnvmTmpPath));
    }
}