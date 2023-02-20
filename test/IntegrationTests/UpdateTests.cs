using System.Diagnostics;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using Serde;
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
        // This will download the new version and run the installer, which should
        // replace the old version. However, the endpoint is set to serve a shell
        // script instead. The shell script will print a message to stdout, which
        // we can check for.
        var proc = Process.Start(new ProcessStartInfo() {
            FileName = dnvmTmpPath,
            Arguments = $"update --self -v --dnvm-url {mockServer.PrefixString}releases.json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        proc!.WaitForExit();
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        Assert.Equal("", error);
        Assert.Contains("Hello from dnvm test", output);
        Assert.Equal(0, proc.ExitCode);
    }

    [Fact]
    public async Task RunUpdateSelfInstaller()
    {
        using var srcTmpDir = TestUtils.CreateTempDirectory();
        using var dnvmHome = TestUtils.CreateTempDirectory();
        var dnvmTmpPath = srcTmpDir.CopyFile(SelfInstallTests.DnvmExe);

        // Create a dest dnvm home that looks like a previous install
        const string helloString = "Hello from dnvm test";
        var prevDnvmPath = Path.Combine(dnvmHome.Path, Utilities.DnvmExeName);
        Assets.MakeFakeExe(prevDnvmPath, helloString);
        // Create a fake dotnet
        var sdkDir = Path.Combine(dnvmHome.Path, "dn");
        Directory.CreateDirectory(sdkDir);
        var fakeDotnet = Path.Combine(sdkDir, Utilities.DotnetExeName);
        Assets.MakeFakeExe(fakeDotnet, "Hello from dotnet test");
        _ = await ProcUtil.RunWithOutput("chmod", $"+x {fakeDotnet}");

        var startVer = Program.SemVer;
        var result = await ProcUtil.RunWithOutput(
            dnvmTmpPath,
            $"install --self -v --update",
            new() { ["DNVM_HOME"] = dnvmHome.Path });

        Assert.Equal(0, result.ExitCode);

        // The old exe should have been moved to the new location
        Assert.False(File.Exists(dnvmTmpPath));

        using (var newDnvmStream = File.OpenRead(prevDnvmPath))
        using (var tmpDnvmStream = File.OpenRead(SelfInstallTests.DnvmExe))
        {
            var newFileHash = await SHA1.HashDataAsync(newDnvmStream);
            var oldFileHash = await SHA1.HashDataAsync(tmpDnvmStream);
            Assert.Equal(newFileHash, oldFileHash);
        }
        // Self-install update does not modify the manifest (or create one if it doesn't exist)
        Assert.False(File.Exists(Path.Combine(dnvmHome.Path, GlobalOptions.ManifestFileName)));
        if (!OperatingSystem.IsWindows())
        {
            // Updated env file should be created
            var envPath = Path.Combine(dnvmHome.Path, "env");
            Assert.True(File.Exists(envPath));
            // source the sh script and confirm that dnvm and dotnet are on the path
            var src = $"""
set -e
. "{envPath}"
echo "dnvm: `which dnvm`"
echo "dotnet: `which dotnet`"
echo "DOTNET_ROOT: $DOTNET_ROOT"
""";
            var shellResult = await ProcUtil.RunShell(src);

            Assert.Contains("dnvm: " + prevDnvmPath, shellResult.Out);
            Assert.Contains("dotnet: " + Path.Combine(dnvmHome.Path, Utilities.DotnetExeName), shellResult.Out);
            Assert.Contains("DOTNET_ROOT: " + sdkDir, shellResult.Out);
        }
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