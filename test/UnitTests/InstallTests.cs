
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
}