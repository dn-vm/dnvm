
using Zio;
using Zio.FileSystems;

namespace Dnvm.Test;

public sealed class TestEnv : IDisposable
{
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _workingDir = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();

    public DnvmEnv DnvmEnv { get; init; }

    public TestEnv(
        string dotnetFeedUrl,
        string releasesUrl,
        UPath? cwdOpt = null)
    {
        var cwd = cwdOpt ?? UPath.Root;

        var physicalFs = DnvmEnv.PhysicalFs;
        var homeFs = new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(_dnvmHome.Path));
        var cwdFs = new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(_workingDir.Path));
        cwdFs.CreateDirectory(cwd);

        DnvmEnv = new DnvmEnv(
                userHome: _userHome.Path,
                homeFs,
                cwdFs,
                cwd,
                isPhysical: true,
                getUserEnvVar: s => _envVars[s],
                setUserEnvVar: (name, val) => _envVars[name] = val,
                _dnvmHome.Path,
                [ dotnetFeedUrl ],
                releasesUrl);
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
        DnvmEnv.Dispose();
    }
}