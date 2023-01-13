
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Dnvm.Test;

public sealed class SelfInstallTests
{
    internal static readonly string DnvmExe = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "dnvm_aot/dnvm" + (Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : ""));

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
    //[Fact]
    //public async Task DnvmHomeAndInstallCanBeDifferent()
    //{
    //    using var installDir = TestUtils.CreateTempDirectory();
    //    using var server = new MockServer();
    //    var options = new CommandArguments.InstallArguments()
    //    {
    //        Channel = Channel.Lts,
    //        FeedUrl = server.PrefixString,
    //        DnvmInstallPath = installDir.Path,
    //        UpdateUserEnvironment = false,
    //    };
    //    var installCmd = new Install(_globalOptions, _logger, options);
    //    Assert.Equal(Result.Success, await installCmd.Run());
    //    Assert.Equal(_globalOptions, Path.GetDirectoryName(installCmd.SdkInstallDir));
    //    Assert.True(File.Exists(Path.Combine(installCmd.SdkInstallDir, "dotnet")));
    //    Assert.True(File.Exists(Path.Combine(_globalOptions, ManifestUtils.FileName)));
    //    Assert.False(File.Exists(Path.Combine(installDir.Path, ManifestUtils.FileName)));
    //}

    [ConditionalFact(typeof(UnixOnly))]
    public async Task FirstRunWritesEnv()
    {
        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = "install --self -v",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["HOME"] = _globalOptions.UserHome;
        psi.Environment["DNVM_HOME"] = _globalOptions.DnvmHome;
        var proc = Process.Start(psi);
        await proc!.WaitForExitAsync();

        string envPath = Path.Combine(_globalOptions.DnvmHome, "env");
        Assert.True(File.Exists(envPath));
        // source the sh script and confirm that dnvm and dotnet are on the path
        var src = $"""
set -e
. "{envPath}"
echo "dnvm: `which dnvm`"
echo "PATH: $PATH"
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
        Assert.Contains($":{_globalOptions.SdkInstallDir}:", $":{await ReadLine("PATH: ")}");
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
        const string PATH = "PATH";
        const string DOTNET_ROOT = "DOTNET_ROOT";

        var psi = new ProcessStartInfo
        {
            FileName = DnvmExe,
            Arguments = "install --self -v",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
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