
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Dnvm.Test;

public sealed class SelfInstallTests
{
    internal static readonly string DnvmExe = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "dnvm_aot",
        "dnvm" + Utilities.ExeSuffix);

    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;

    public SelfInstallTests()
    {
        _globalOptions = new() {
            DnvmHome = _dnvmHome.Path,
            UserHome = _userHome.Path,
            GetUserEnvVar = s => _envVars[s],
            SetUserEnvVar = (name, val) => _envVars[name] = val,
        };
    }

    [Fact]
    public async Task FirstRunInstallsDotnet()
    {
        await using var mockServer = new MockServer();
        var procResult = await ProcUtil.RunWithOutput(DnvmExe,
            $"install --self --feed-url {mockServer.PrefixString} -y -v",
            new() {
                ["HOME"] = _globalOptions.UserHome,
                ["DNVM_HOME"] = _globalOptions.DnvmHome
            }
        );
        Assert.Equal(0, procResult.ExitCode);

        var dotnetPath = Path.Combine(_globalOptions.SdkInstallDir, $"dotnet{Utilities.ExeSuffix}");
        Assert.True(File.Exists(dotnetPath));

        var result = await ProcUtil.RunWithOutput(dotnetPath, "-h");
        Assert.Contains(Assets.ArchiveToken, result.Out);
    }

    [ConditionalFact(typeof(UnixOnly))]
    public async Task FirstRunWritesEnv()
    {
        await using var mockServer = new MockServer();
        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = $"install --self --feed-url {mockServer.PrefixString} -y -v",
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
        Assert.Equal(_globalOptions.SdkInstallDir, Path.GetDirectoryName(await ReadLine("dotnet: ")));
        Assert.Equal(_globalOptions.SdkInstallDir, await ReadLine("DOTNET_ROOT: "));

        async Task<string> ReadLine(string expectedPrefix)
        {
            var s = await proc.StandardOutput.ReadLineAsync();
            Assert.NotNull(s);
            Assert.StartsWith(expectedPrefix, s);
            return s![expectedPrefix.Length..];
        }
    }

    [ConditionalFact(typeof(WindowsOnly))]
    public async Task FirstRunSetsUserPath()
    {
        await using var mockServer = new MockServer();
        const string PATH = "PATH";
        const string DOTNET_ROOT = "DOTNET_ROOT";

        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = $"install --self --feed-url {mockServer.PrefixString} -y -v",
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
            Assert.Contains($";{_globalOptions.SdkInstallDir};", pathMatch);
            Assert.Equal(_globalOptions.SdkInstallDir, Environment.GetEnvironmentVariable(DOTNET_ROOT, EnvironmentVariableTarget.User));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PATH, savedPath, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(DOTNET_ROOT, savedDotnetRoot, EnvironmentVariableTarget.User);
        }
    }
}