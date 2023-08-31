
using Zio.FileSystems;

namespace Dnvm.Test;

public sealed class TestEnv : IDisposable
{
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();

    public DnvmEnv DnvmEnv { get; init; }

    public TestEnv(string dotnetFeedUrl, string releasesUrl)
    {
        DnvmEnv = new DnvmEnv(
                userHome: _userHome.Path,
                new MemoryFileSystem(),
                getUserEnvVar: s => _envVars[s],
                setUserEnvVar: (name, val) => _envVars[name] = val);
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
        DnvmEnv.Dispose();
    }
}