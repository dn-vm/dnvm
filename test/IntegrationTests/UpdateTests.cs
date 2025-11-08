using Spectre.Console.Testing;
using Xunit;

namespace Dnvm.Test;

public sealed class UpdateTests
{
    private Task TestWithServer(Func<MockServer, DnvmEnv, Task> test)
    {
        return TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl);
            await test(mockServer, testOptions.DnvmEnv);
        });
    }

    [Fact]
    public Task SelfUpdateNewVersion() => TestWithServer(async (mockServer, env) =>
    {
        var startVer = Program.SemVer;
        mockServer.DnvmReleases = mockServer.DnvmReleases with {
            LatestVersion = mockServer.DnvmReleases.LatestVersion with {
                Version = startVer.WithMajor(startVer.Major + 1).ToString()
            }
        };
        var proc = await DnvmRunner.RunAndRestoreEnv(env, SelfInstallTests.DnvmExe,
            $"update --self -v --dnvm-url {mockServer.DnvmReleasesUrl}");
        var output = proc.Out;
        var error = proc.Error;
        Assert.Contains("Hello from dnvm test", output);
        Assert.Equal(0, proc.ExitCode);
    });

    [Fact]
    public async Task EnablePreviewsAndDownload() => await TestWithServer(async (mockServer, env) =>
    {
        var startVer = Program.SemVer;
        mockServer.DnvmReleases = mockServer.DnvmReleases with {
            LatestVersion = mockServer.DnvmReleases.LatestVersion with {
                Version = startVer.ToString() // Keep the same version
            },
            LatestPreview = mockServer.DnvmReleases.LatestVersion with {
                Version = startVer.WithMajor(startVer.Major + 1).WithPrerelease("preview").ToString()
            }
        };
        var proc = await DnvmRunner.RunAndRestoreEnv(env, SelfInstallTests.DnvmExe,
            $"update --self -v --dnvm-url {mockServer.DnvmReleasesUrl}");
        var output = proc.Out;
        var error = proc.Error;
        Assert.Contains("dnvm is up-to-date", output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, proc.ExitCode);

        // Manually create config file with previews enabled
        var config = new DnvmConfig { PreviewsEnabled = true };
        DnvmConfigFile.Write(config);

        proc = await DnvmRunner.RunAndRestoreEnv(env, SelfInstallTests.DnvmExe,
            $"update --self -v --dnvm-url {mockServer.DnvmReleasesUrl}");
        output = proc.Out;
        error = proc.Error;
        Assert.Contains("Hello from dnvm test", output);
        Assert.Equal(0, proc.ExitCode);
    });

    [Fact]
    public Task SelfUpdateUpToDate() => TestWithServer(async (mockServer, env) =>
    {
        mockServer.DnvmReleases = mockServer.DnvmReleases with {
            LatestVersion = mockServer.DnvmReleases.LatestVersion with {
                Version = Program.SemVer.ToString() // report the same version as installed
            }
        };
        var result = await DnvmRunner.RunAndRestoreEnv(env, SelfInstallTests.DnvmExe,
            $"update --self -v --dnvm-url {mockServer.DnvmReleasesUrl}");
        var output = result.Out;
        var error = result.Error;
        Assert.Equal(0, result.ExitCode);
        result = await DnvmRunner.RunAndRestoreEnv(env, SelfInstallTests.DnvmExe, "-h");
        Assert.DoesNotContain("Hello from dnvm test", result.Out);
        Assert.Equal(0, result.ExitCode);
    });

    [Fact]
    public async Task ValidateBinary()
    {
        using var tmpDir = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = tmpDir.CopyFile(SelfInstallTests.DnvmExe);
        Assert.True(await UpdateCommand.ValidateBinary(new TestConsole(), logger: null, dnvmTmpPath));
    }
}