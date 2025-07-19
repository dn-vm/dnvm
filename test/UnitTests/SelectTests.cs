using Semver;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Dnvm.Test;

public sealed class SelectTests
{
    private readonly Logger _logger;

    public SelectTests(ITestOutputHelper output)
    {
        _logger = new Logger(new StringWriter());
    }

    [Fact]
    public Task SelectPreview() => TestUtils.RunWithServer(async (mockServer, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var homeFs = env.DnvmHomeFs;
        var defaultSdkDir = DnvmEnv.DefaultSdkDirName;
        var defaultDotnet = DnvmEnv.GetSdkPath(defaultSdkDir) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(defaultDotnet));
        result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Preview(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var previewDotnet = DnvmEnv.GetSdkPath(defaultSdkDir) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(previewDotnet));

        if (!OperatingSystem.IsWindows())
        {
            // Check that the dotnet link/cmd points to the default SDK
            AssertSdkDir(defaultSdkDir, env);
        }

        // Select the preview SDK
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal(defaultSdkDir, manifest.CurrentSdkDir);

        manifest = (await SelectCommand.RunWithManifest(env, defaultSdkDir, manifest, _logger)).Unwrap();

        Assert.Equal(defaultSdkDir, manifest.CurrentSdkDir);
        AssertSdkDir(defaultSdkDir, env);
    });

    [Fact]
    public Task BadDirName() => TestUtils.RunWithServer(async (server, env) =>
    {
        var trackResult = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
            SdkDir = new SdkDirName("dn"),
        });
        Assert.Equal(TrackCommand.Result.Success, trackResult);
        var console = (TestConsole)env.Console;
        var prefixLen = console.Output.Length;
        var selectResult = await SelectCommand.Run(env, _logger, new SdkDirName("bad"));
        Assert.Equal(SelectCommand.Result.BadDirName, selectResult);

        Assert.Equal("""

Error: Invalid SDK directory name: bad

Valid SDK directory names:
  dn

""".Replace(Environment.NewLine, "\n"), console.Output[prefixLen..]);

    });

    [Fact]
    public Task SelectSingleNumberDirName() => TestUtils.RunWithServer(async (mockServer, env) =>
    {
        // Create SDK directories with single number names
        var sdk8DirName = new SdkDirName("8");
        var sdk9DirName = new SdkDirName("9");

        // Install SDKs into directories with these names
        var result = await InstallCommand.Run(env, _logger, new InstallCommand.Options
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            SdkDir = sdk8DirName
        });
        Assert.Equal(InstallCommand.Result.Success, result);

        // Install a different version for directory "9"
        var version9 = new SemVersion(42, 42, 43);
        mockServer.RegisterReleaseVersion(version9, "lts", "active");
        result = await InstallCommand.Run(env, _logger, new InstallCommand.Options
        {
            SdkVersion = version9,
            SdkDir = sdk9DirName
        });
        Assert.Equal(InstallCommand.Result.Success, result);

        // Verify the directories were created
        var homeFs = env.DnvmHomeFs;
        var sdk8DotnetPath = DnvmEnv.GetSdkPath(sdk8DirName) / Utilities.DotnetExeName;
        var sdk9DotnetPath = DnvmEnv.GetSdkPath(sdk9DirName) / Utilities.DotnetExeName;
        Assert.True(homeFs.FileExists(sdk8DotnetPath));
        Assert.True(homeFs.FileExists(sdk9DotnetPath));

        // Read the manifest to verify SDKs were installed
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Contains(manifest.InstalledSdks, s => s.SdkDirName.Name == "8" && s.SdkVersion == MockServer.DefaultLtsVersion);
        Assert.Contains(manifest.InstalledSdks, s => s.SdkDirName.Name == "9" && s.SdkVersion == version9);

        // Test selecting the "8" directory
        var selectResult = await SelectCommand.Run(env, _logger, sdk8DirName);
        Assert.Equal(SelectCommand.Result.Success, selectResult);
        manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal(sdk8DirName, manifest.CurrentSdkDir);
        AssertSdkDir(sdk8DirName, env);

        // Test selecting the "9" directory
        selectResult = await SelectCommand.Run(env, _logger, sdk9DirName);
        Assert.Equal(SelectCommand.Result.Success, selectResult);
        manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal(sdk9DirName, manifest.CurrentSdkDir);
        AssertSdkDir(sdk9DirName, env);
    });

    private static void AssertSdkDir(
        SdkDirName dirName,
        DnvmEnv env)
    {
        if (OperatingSystem.IsWindows())
        {
            // On Windows read the PATH environment variable and check that it contains the target SDK directory
            var path = env.GetUserEnvVar("PATH") ?? string.Empty;
            Assert.Contains(env.RealPath(DnvmEnv.GetSdkPath(dirName)), path.Split(";"));
        }
        else
        {
            // On unix read the target symlink and check that it points to the correct directory
            var dotnetSymlinkPath = env.RealPath(DnvmEnv.DotnetSymlinkPath);
            var dnxSymlinkPath = env.RealPath(DnvmEnv.DnxSymlinkPath);
            var finfo = new FileInfo(dotnetSymlinkPath);
            Assert.EndsWith(Path.Combine(dirName.Name, Utilities.DotnetExeName), finfo.LinkTarget);
            finfo = new FileInfo(dnxSymlinkPath);
            Assert.EndsWith(Path.Combine(dirName.Name, Utilities.DnxScriptName), finfo.LinkTarget);
        }
    }
}
