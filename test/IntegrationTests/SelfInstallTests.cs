
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;
using Zio;
using static Dnvm.Test.TestUtils;
using static System.Environment;

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
    public Task FirstRunInstallsDotnet() => RunWithServer(async (mockServer, env) =>
    {
        var procResult = await ProcUtil.RunWithOutput(DnvmExe,
            $"selfinstall --feed-url {mockServer.PrefixString} -y -v",
            new() {
                ["HOME"] = env.UserHome,
                ["DNVM_HOME"] = env.RealPath(UPath.Root)
            }
        );
        _testOutput.WriteLine(procResult.Out);
        _testOutput.WriteLine(procResult.Error);
        Assert.Equal(0, procResult.ExitCode);

        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        var dotnetPath = sdkInstallDir / Utilities.DotnetExeName;
        Assert.True(env.Vfs.FileExists(dotnetPath));

        var result = await ProcUtil.RunWithOutput(env.RealPath(dotnetPath), "-h");
        Assert.Contains(Assets.ArchiveToken, result.Out);
    });

    [ConditionalFact(typeof(UnixOnly))]
    public Task SelfInstallDialog() => RunWithServer(async (mockServer, env) =>
    {
        var buffer = new char[1024];
        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = $"selfinstall --feed-url {mockServer.PrefixString} -v",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            EnvironmentVariables = {
                ["HOME"] = env.UserHome,
                ["DNVM_HOME"] = env.RealPath(UPath.Root)
            }
        };
        var proc = Process.Start(psi)!;
        // Discard the first two lines -- they're the prolog
        for (int i = 0; i < 2; i++)
        {
            _ = await proc.StandardOutput.ReadLineAsync() + Environment.NewLine;
        }
        var lines = "";
        for (int i = 0; i < 2; i++)
        {
            lines += await proc.StandardOutput.ReadLineAsync() + Environment.NewLine;
        }
        Assert.Equal($"""
Starting dnvm install
Please select install location [default: {env.RealPath(UPath.Root)}]:
""", lines.Trim());

        // Flush output
        await proc.StandardOutput.ReadAsync(buffer);
        // Write empty line -- use the default
        await proc.StandardInput.WriteLineAsync();

        lines = "";
        for (int i = 0; i < 8; i++)
        {
            lines += await proc.StandardOutput.ReadLineAsync() + Environment.NewLine;
        }
        Assert.Equal("""
Which channel would you like to start tracking?
Available channels:
	1) Latest - The latest supported version from either the LTS or STS support channels.
	2) STS - The latest version in Short-Term support
	3) LTS - The latest version in Long-Term support
	4) Preview - The latest preview version

Please select a channel [default: Latest]:
""", lines.Trim());

        // Flush output
        await proc.StandardOutput.ReadAsync(buffer);
        // Use 'preview'
        await proc.StandardInput.WriteLineAsync("4");

        lines = await proc.StandardOutput.ReadLineAsync();
        Assert.Equal("One or more paths are missing from the user environment. Attempt to update the user environment?", lines);

        // Flush output
        await proc.StandardOutput.ReadAsync(buffer);
        // Say yes
        await proc.StandardInput.WriteLineAsync("y");

        lines = "";
        for (int i = 0; i < 8; i++)
        {
            lines += await proc.StandardOutput.ReadLineAsync();
        }

        Assert.Equal($"""
Proceeding with installation.
Log: Location of running exe: {DnvmExe}
Log: Copying file from '{DnvmExe}' to '{DnvmEnv.DnvmExePath}'
Dnvm installed successfully.
Found latest version: 99.99.99-preview
""".RemoveWhitespace(), lines.RemoveWhitespace());

        do
        {
            lines = await proc.StandardOutput.ReadLineAsync();
        } while (lines != null && !lines.Contains("Writing env sh file"));

        lines = await proc.StandardOutput.ReadToEndAsync();
        Assert.Equal($"""
Log: Setting environment variables in shell files
Scanning for shell files to update
Log: Checking for file: {env.UserHome}/.profile
Log: Checking for file: {env.UserHome}/.bashrc
Log: Checking for file: {env.UserHome}/.zshrc
""".RemoveWhitespace(), lines.RemoveWhitespace());
        Assert.Contains(env.RealPath(UPath.Root), env.Vfs.ReadAllText(DnvmEnv.EnvPath));
    });

    [ConditionalFact(typeof(UnixOnly))]
    public Task FirstRunWritesEnv() => RunWithServer(async (mockServer, env) =>
    {
        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = $"selfinstall --feed-url {mockServer.PrefixString} -y -v",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["HOME"] = env.UserHome;
        psi.Environment["DNVM_HOME"] = env.RealPath(UPath.Root);
        var proc = Process.Start(psi);
        await proc!.WaitForExitAsync();
        Assert.Equal(0, proc.ExitCode);

        Assert.True(env.Vfs.FileExists(DnvmEnv.EnvPath));
        var envPath = env.RealPath(DnvmEnv.EnvPath);
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

        var dnvmHome = env.RealPath(UPath.Root);
        Assert.Equal(dnvmHome, Path.GetDirectoryName(await ReadLine("dnvm: ")));
        Assert.Equal(dnvmHome, Path.GetDirectoryName(await ReadLine("dotnet: ")));
        var sdkInstallDir = env.RealPath(DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName));
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
    public Task FirstRunSetsUserPath() => RunWithServer(async (mockServer, env) =>
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
        psi.Environment["HOME"] = env.UserHome;
        psi.Environment["DNVM_HOME"] = env.RealPath(UPath.Root);

        var savedPath = Environment.GetEnvironmentVariable(PATH, EnvironmentVariableTarget.User);
        var savedDotnetRoot = Environment.GetEnvironmentVariable(DOTNET_ROOT, EnvironmentVariableTarget.User);
        try
        {
            var proc = Process.Start(psi);
            await proc!.WaitForExitAsync();

            var pathMatch = $";{Environment.GetEnvironmentVariable(PATH, EnvironmentVariableTarget.User)};";
            Assert.Contains($";{env.RealPath(UPath.Root)};", pathMatch);
            var sdkInstallDir = env.RealPath(DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName));
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
    public Task RealUpdateSelf() => RunWithServer(async (mockServer, env) =>
    {
        var copiedExe = env.RealPath(DnvmEnv.DnvmExePath);
        File.Copy(DnvmExe, copiedExe);
        using var tmpDir = TestUtils.CreateTempDirectory();
        mockServer.DnvmPath = Assets.MakeZipOrTarball(env.RealPath(UPath.Root), Path.Combine(tmpDir.Path, "dnvm"));

        var timeBeforeUpdate = File.GetLastWriteTimeUtc(copiedExe);
        var result = await ProcUtil.RunWithOutput(
            copiedExe,
            $"update --self --dnvm-url {mockServer.DnvmReleasesUrl} -v",
            new() {
                ["HOME"] = env.UserHome,
                ["DNVM_HOME"] = env.RealPath(UPath.Root)
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
        using var dnvmFs = DnvmEnv.CreateDefault(dnvmHome.Path);
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

        _testOutput.WriteLine(result.Out);
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