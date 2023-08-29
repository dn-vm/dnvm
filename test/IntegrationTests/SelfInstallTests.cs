
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;
using Zio;
using static Dnvm.Test.TestUtils;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Dnvm.Test;

public sealed class SelfInstallTests
{
    internal static readonly string DnvmExe = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "dnvm_aot",
        Utilities.DnvmExeName);

    private readonly ITestOutputHelper _testOutput;

    public SelfInstallTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
    }

    [Fact]
    public Task FirstRunInstallsDotnet() => RunWithServer(async (mockServer, globalOptions) =>
    {
        var env = globalOptions.DnvmEnv;
        var procResult = await ProcUtil.RunWithOutput(DnvmExe,
            $"selfinstall --feed-url {mockServer.PrefixString} -y -v",
            new() {
                ["HOME"] = globalOptions.UserHome,
                ["DNVM_HOME"] = env.RealPath(UPath.Root)
            }
        );
        _testOutput.WriteLine(procResult.Out);
        _testOutput.WriteLine(procResult.Error);
        Assert.Equal(0, procResult.ExitCode);

        var sdkInstallDir = DnvmEnv.GetSdkPath(GlobalOptions.DefaultSdkDirName);
        var dotnetPath = sdkInstallDir / Utilities.DotnetExeName;
        Assert.True(env.Vfs.FileExists(dotnetPath));

        var result = await ProcUtil.RunWithOutput(env.RealPath(dotnetPath), "-h");
        Assert.Contains(Assets.ArchiveToken, result.Out);
    });

    [ConditionalFact(typeof(UnixOnly))]
    public Task FirstRunWritesEnv() => RunWithServer(async (mockServer, globalOptions) =>
    {
        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = $"selfinstall --feed-url {mockServer.PrefixString} -y -v",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["HOME"] = globalOptions.UserHome;
        psi.Environment["DNVM_HOME"] = globalOptions.DnvmEnv.RealPath(UPath.Root);
        var proc = Process.Start(psi);
        await proc!.WaitForExitAsync();
        Assert.Equal(0, proc.ExitCode);

        Assert.True(globalOptions.DnvmEnv.Vfs.FileExists(DnvmEnv.EnvPath));
        var envPath = globalOptions.DnvmEnv.RealPath(DnvmEnv.EnvPath);
        // source the sh script and confirm that dnvm and dotnet are on the path
        var src = $"""
set -e
. "{envPath}"
echo "dnvm: `which dnvm`"
echo "dotnet: `which dotnet`"
echo "DOTNET_ROOT: $DOTNET_ROOT"
""";
        psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        proc = Process.Start(psi)!;
        await proc.StandardInput.WriteAsync(src);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync();
        Assert.Equal(0, proc.ExitCode);

        var dnvmHome = globalOptions.DnvmEnv.RealPath(UPath.Root);
        Assert.Equal(dnvmHome, Path.GetDirectoryName(await ReadLine("dnvm: ")));
        Assert.Equal(dnvmHome, Path.GetDirectoryName(await ReadLine("dotnet: ")));
        var sdkInstallDir = DnvmEnv.GetSdkPath(GlobalOptions.DefaultSdkDirName);
        Assert.Equal(sdkInstallDir, await ReadLine("DOTNET_ROOT: "));

        async Task<string> ReadLine(string expectedPrefix)
        {
            var s = await proc.StandardOutput.ReadLineAsync();
            Assert.NotNull(s);
            Assert.StartsWith(expectedPrefix, s);
            return s![expectedPrefix.Length..];
        }
    });

    [ConditionalFact(typeof(WindowsOnly))]
    public Task FirstRunSetsUserPath() => RunWithServer(async (mockServer, globalOptions) =>
    {
        const string PATH = "PATH";
        const string DOTNET_ROOT = "DOTNET_ROOT";

        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = $"selfinstall --feed-url {mockServer.PrefixString} -y -v",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["HOME"] = globalOptions.UserHome;
        psi.Environment["DNVM_HOME"] = globalOptions.DnvmEnv.RealPath(UPath.Root);

        var savedPath = Environment.GetEnvironmentVariable(PATH, EnvironmentVariableTarget.User);
        var savedDotnetRoot = Environment.GetEnvironmentVariable(DOTNET_ROOT, EnvironmentVariableTarget.User);
        try
        {
            var proc = Process.Start(psi);
            await proc!.WaitForExitAsync();

            var pathMatch = $";{Environment.GetEnvironmentVariable(PATH, EnvironmentVariableTarget.User)};";
            Assert.Contains($";{globalOptions.DnvmEnv.RealPath(UPath.Root)};", pathMatch);
            var sdkInstallDir = DnvmEnv.GetSdkPath(GlobalOptions.DefaultSdkDirName);
            Assert.DoesNotContain($";{sdkInstallDir};", pathMatch);
            Assert.Equal(sdkInstallDir, Environment.GetEnvironmentVariable(DOTNET_ROOT, EnvironmentVariableTarget.User)!);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PATH, savedPath, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(DOTNET_ROOT, savedDotnetRoot, EnvironmentVariableTarget.User);
        }
    });

    [Fact]
    public Task RealUpdateSelf() => RunWithServer(async (mockServer, globalOptions) =>
    {
        var env = globalOptions.DnvmEnv;
        var copiedExe = env.RealPath(DnvmEnv.DnvmExePath);
        File.Copy(DnvmExe, copiedExe);
        using var tmpDir = TestUtils.CreateTempDirectory();
        mockServer.DnvmPath = Assets.MakeZipOrTarball(env.RealPath(UPath.Root), Path.Combine(tmpDir.Path, "dnvm"));

        var timeBeforeUpdate = File.GetLastWriteTimeUtc(copiedExe);
        var result = await ProcUtil.RunWithOutput(
            copiedExe,
            $"update --self --dnvm-url {mockServer.DnvmReleasesUrl} -v",
            new() {
                ["HOME"] = globalOptions.UserHome,
                ["DNVM_HOME"] = globalOptions.DnvmEnv.RealPath(UPath.Root)
            }
        );
        Assert.Equal(0, result.ExitCode);
        var timeAfterUpdate = File.GetLastWriteTimeUtc(copiedExe);
        Assert.True(timeAfterUpdate > timeBeforeUpdate);
        Assert.Contains("Process successfully upgraded", result.Out);
    });

    [Fact]
    public async Task RunUpdateSelfInstaller()
    {
        using var srcTmpDir = TestUtils.CreateTempDirectory();
        using var dnvmHome = TestUtils.CreateTempDirectory();
        using var dnvmFs = DnvmEnv.CreatePhysical(dnvmHome.Path);
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
            $"selfinstall -v --update",
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
        Assert.False(dnvmFs.Vfs.FileExists(DnvmEnv.ManifestPath));
        if (!OperatingSystem.IsWindows())
        {
            // Updated env file should be created
            Assert.True(dnvmFs.Vfs.FileExists(DnvmEnv.EnvPath));
            // source the sh script and confirm that dnvm and dotnet are on the path
            var src = $"""
set -e
. "{dnvmFs.Vfs.ConvertPathToInternal(DnvmEnv.EnvPath)}"
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
}