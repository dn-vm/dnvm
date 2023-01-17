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
    public async Task SelfUpdateNewVersion()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);

        var startVer = Program.SemVer;
        await using var mockServer = new MockServer();
        mockServer.DnvmReleases = mockServer.DnvmReleases with {
            LatestVersion = mockServer.DnvmReleases.LatestVersion with {
                Version = startVer.WithMajor(startVer.Major + 1).ToString()
            }
        };
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
            Arguments = "-h",
            RedirectStandardOutput = true
        });
        proc!.WaitForExit();
        Assert.Contains("Hello from dnvm test", proc.StandardOutput.ReadToEnd());
        Assert.Equal(0, proc.ExitCode);
    }

    [Fact]
    public async Task SelfUpdateUpToDate()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);

        await using var mockServer = new MockServer();
        mockServer.DnvmReleases = mockServer.DnvmReleases with {
            LatestVersion = mockServer.DnvmReleases.LatestVersion with {
                Version = Program.SemVer.ToString() // report the same version as installed
            }
        };
        var result = await ProcUtil.RunWithOutput(
            dnvmTmpPath,
            $"update --self -v --feed-url {mockServer.PrefixString}releases.json");
        var output = result.Out;
        var error = result.Error;
        Assert.Equal("", error);
        Assert.Equal(0, result.ExitCode);
        result = await ProcUtil.RunWithOutput(dnvmTmpPath, "-h");
        Assert.DoesNotContain("Hello from dnvm test", result.Out);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ValidateBinary()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);
        Assert.True(await Update.ValidateBinary(_logger, dnvmTmpPath));
    }
}