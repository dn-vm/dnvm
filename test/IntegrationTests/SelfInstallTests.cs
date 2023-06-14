
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Dnvm.Test;

public sealed class SelfInstallTests
{
    internal static readonly string DnvmExe = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "dnvm_aot",
        Utilities.DnvmExeName);

    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;
    private readonly ITestOutputHelper _testOutput;

    public SelfInstallTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
        _globalOptions = new() {
            DnvmHome = _dnvmHome.Path,
            UserHome = _userHome.Path,
            GetUserEnvVar = s => _envVars[s],
            SetUserEnvVar = (name, val) => _envVars[name] = val,
        };
    }

    private static ValueTask TestWithServer(Func<MockServer, ValueTask> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            await test(mockServer);
        });

    [Fact]
    public ValueTask FirstRunInstallsDotnet() => TestWithServer(async mockServer =>
    {
        var procResult = await ProcUtil.RunWithOutput(DnvmExe,
            $"selfinstall --feed-url {mockServer.PrefixString} -y -v",
            new() {
                ["HOME"] = _globalOptions.UserHome,
                ["DNVM_HOME"] = _globalOptions.DnvmHome
            }
        );
        _testOutput.WriteLine(procResult.Out);
        _testOutput.WriteLine(procResult.Error);
        Assert.Equal(0, procResult.ExitCode);

        var sdkInstallDir = Path.Combine(_globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name);
        var dotnetPath = Path.Combine(sdkInstallDir, $"dotnet{Utilities.ExeSuffix}");
        Assert.True(File.Exists(dotnetPath));

        var result = await ProcUtil.RunWithOutput(dotnetPath, "-h");
        Assert.Contains(Assets.ArchiveToken, result.Out);
    });

    [ConditionalFact(typeof(UnixOnly))]
    public ValueTask FirstRunWritesEnv() => TestWithServer(async mockServer =>
    {
        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = $"selfinstall --feed-url {mockServer.PrefixString} -y -v",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["HOME"] = _globalOptions.UserHome;
        psi.Environment["DNVM_HOME"] = _globalOptions.DnvmHome;
        var proc = Process.Start(psi);
        await proc!.WaitForExitAsync();
        Assert.Equal(0, proc.ExitCode);

        string envPath = Path.Combine(_globalOptions.DnvmHome, "env");
        Assert.True(File.Exists(envPath));
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

        Assert.Equal(_globalOptions.DnvmInstallPath, Path.GetDirectoryName(await ReadLine("dnvm: ")));
        Assert.Equal(_globalOptions.DnvmInstallPath, Path.GetDirectoryName(await ReadLine("dotnet: ")));
        var sdkInstallDir = Path.Combine(_globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name);
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
    public ValueTask FirstRunSetsUserPath() => TestWithServer(async mockServer =>
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
        psi.Environment["HOME"] = _globalOptions.UserHome;
        psi.Environment["DNVM_HOME"] = _globalOptions.DnvmHome;

        var savedPath = Environment.GetEnvironmentVariable(PATH, EnvironmentVariableTarget.User);
        var savedDotnetRoot = Environment.GetEnvironmentVariable(DOTNET_ROOT, EnvironmentVariableTarget.User);
        try
        {
            var proc = Process.Start(psi);
            await proc!.WaitForExitAsync();

            var pathMatch = $";{Environment.GetEnvironmentVariable(PATH, EnvironmentVariableTarget.User)};";
            Assert.Contains($";{_globalOptions.DnvmInstallPath};", pathMatch);
            var sdkInstallDir = Path.Combine(_globalOptions.DnvmHome, GlobalOptions.DefaultSdkDirName.Name);
            Assert.DoesNotContain($";{sdkInstallDir};", pathMatch);
            Assert.Equal(sdkInstallDir, Environment.GetEnvironmentVariable(DOTNET_ROOT, EnvironmentVariableTarget.User));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PATH, savedPath, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(DOTNET_ROOT, savedDotnetRoot, EnvironmentVariableTarget.User);
        }
    });

    [Fact]
    public ValueTask RealUpdateSelf() => TestWithServer(async mockServer =>
    {
        var copiedExe = Path.Combine(_globalOptions.DnvmHome, Utilities.DnvmExeName);
        File.Copy(DnvmExe, copiedExe);
        using var tmpDir = TestUtils.CreateTempDirectory();
        mockServer.DnvmPath = Assets.MakeZipOrTarball(_globalOptions.DnvmHome, Path.Combine(tmpDir.Path, "dnvm"));

        var timeBeforeUpdate = File.GetLastWriteTimeUtc(copiedExe);
        var result = await ProcUtil.RunWithOutput(
            copiedExe,
            $"update --self --dnvm-url {mockServer.DnvmReleasesUrl} -v",
            new() {
                ["HOME"] = _globalOptions.UserHome,
                ["DNVM_HOME"] = _globalOptions.DnvmHome
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
        using var dnvmFs = DnvmFs.CreatePhysical(dnvmHome.Path);
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
        Assert.False(dnvmFs.Vfs.FileExists(DnvmFs.ManifestPath));
        if (!OperatingSystem.IsWindows())
        {
            // Updated env file should be created
            Assert.True(dnvmFs.Vfs.FileExists(DnvmFs.EnvPath));
            // source the sh script and confirm that dnvm and dotnet are on the path
            var src = $"""
set -e
. "{dnvmFs.Vfs.ConvertPathToInternal(DnvmFs.EnvPath)}"
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