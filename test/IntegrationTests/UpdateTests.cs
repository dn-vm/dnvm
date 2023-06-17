using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class UpdateTests
{
    private ValueTask TestWithServer(Func<MockServer, ValueTask> test)
    {
        return TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            await test(mockServer);
        });
    }

    [Fact]
    public ValueTask SelfUpdateNewVersion() => TestWithServer(async mockServer =>
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        using var dnvmHome = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);

        var startVer = Program.SemVer;
        mockServer.DnvmReleases = mockServer.DnvmReleases with {
            LatestVersion = mockServer.DnvmReleases.LatestVersion with {
                Version = startVer.WithMajor(startVer.Major + 1).ToString()
            }
        };
        // This will download the new version and run the installer, which should
        // replace the old version. However, the endpoint is set to serve a shell
        // script instead. The shell script will print a message to stdout, which
        // we can check for.
        var proc = await ProcUtil.RunWithOutput(dnvmTmpPath,
            $"update --self -v --dnvm-url {mockServer.DnvmReleasesUrl}",
            new() { ["DNVM_HOME"] = dnvmHome.Path });
        var output = proc.Out;
        var error = proc.Error;
        Assert.Equal("", error);
        Assert.Contains("Hello from dnvm test", output);
        Assert.Equal(0, proc.ExitCode);
    });

    [Fact]
    public ValueTask SelfUpdateUpToDate() => TestWithServer(async mockServer =>
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        using var dnvmHome = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);

        mockServer.DnvmReleases = mockServer.DnvmReleases with {
            LatestVersion = mockServer.DnvmReleases.LatestVersion with {
                Version = Program.SemVer.ToString() // report the same version as installed
            }
        };
        var result = await ProcUtil.RunWithOutput(
            dnvmTmpPath,
            $"update --self -v --feed-url {mockServer.DnvmReleasesUrl}",
            new() { ["DNVM_HOME"] = dnvmHome.Path });
        var output = result.Out;
        var error = result.Error;
        Assert.Equal("", error);
        Assert.Equal(0, result.ExitCode);
        result = await ProcUtil.RunWithOutput(dnvmTmpPath, "-h");
        Assert.DoesNotContain("Hello from dnvm test", result.Out);
        Assert.Equal(0, result.ExitCode);
    });

    [Fact]
    public async Task ValidateBinary()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);
        Assert.True(await UpdateCommand.ValidateBinary(null, dnvmTmpPath));
    }
}