
using System.IO;
using Spectre.Console.Testing;
using Zio;
using Zio.FileSystems;

namespace Dnvm.Test;

public sealed class TestEnv : IDisposable
{
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _workingDir = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _configDir = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();

    public DnvmEnv DnvmEnv { get; init; }
    public string ConfigDirPath => _configDir.Path;

    public TestEnv(
        string dotnetFeedUrl,
        string releasesUrl,
        UPath? cwdOpt = null)
    {
        var cwd = cwdOpt ?? UPath.Root;

        var physicalFs = DnvmEnv.PhysicalFs;
        var dnvmFs = new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(_dnvmHome.Path));
        var cwdFs = new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(_workingDir.Path));
        cwdFs.CreateDirectory(cwd);

        // Set the test config directory
        DnvmConfigFile.TestConfigDirectory = _configDir.Path;

        DnvmEnv = new DnvmEnv(
                userHome: _userHome.Path,
                dnvmFs,
                cwdFs,
                cwd,
                isPhysical: true,
                getUserEnvVar: s => _envVars.TryGetValue(s, out var val) ? val : null,
                setUserEnvVar: (name, val) => _envVars[name] = val,
                new TestConsole(),
                [ dotnetFeedUrl ],
                releasesUrl);
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
        _workingDir.Dispose();
        _configDir.Dispose();
        DnvmConfigFile.TestConfigDirectory = null;
        DnvmEnv.Dispose();
    }
}