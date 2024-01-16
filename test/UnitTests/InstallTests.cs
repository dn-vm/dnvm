
using Spectre.Console.Testing;
using Xunit;
using Zio;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class InstallTests
{
    [Fact]
    public Task LtsInstall() => RunWithServer(async (server, env) =>
    {
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        var dotnetFile = sdkInstallDir / Utilities.DotnetExeName;
        Assert.False(env.Vfs.FileExists(dotnetFile));

        var logger = new Logger(new TestConsole());
        var options = new CommandArguments.InstallArguments()
        {
            SdkVersion = MockServer.DefaultLtsVersion
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.True(env.Vfs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.Vfs.ReadAllText(dotnetFile));

        var manifest = await env.ReadManifest();
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);
    });

    [Fact]
    public Task AlreadyInstalled() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var options = new CommandArguments.InstallArguments()
        {
            SdkVersion = MockServer.DefaultLtsVersion,
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await env.ReadManifest();
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);

        installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.Contains("already installed", console.Output);
    });

    [Fact]
    public Task AlreadyInstalledForce() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var options = new CommandArguments.InstallArguments()
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            Force = true,
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await env.ReadManifest();
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);

        console.Clear(home: false);
        installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.DoesNotContain("already installed", console.Output);
    });

    [Fact]
    public Task InstallProgressInConsole() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var options = new CommandArguments.InstallArguments()
        {
            SdkVersion = MockServer.DefaultLtsVersion,
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        // If the progress bar is displayed, there should be at least two occurrences
        var index = console.Output.IndexOf("Downloading SDK");
        Assert.NotEqual(-1, index);
        index = console.Output.Substring(index + 1).IndexOf("Downloading SDK");
        Assert.NotEqual(-1, index);
    });
}