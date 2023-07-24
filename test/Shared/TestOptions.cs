
using Zio.FileSystems;

namespace Dnvm.Test;

public sealed class TestOptions : IDisposable
{
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();

    public GlobalOptions GlobalOptions { get; init; }

    public TestOptions()
    {
        GlobalOptions = new GlobalOptions(
            dnvmHome: _dnvmHome.Path,
            userHome: _userHome.Path,
            getUserEnvVar: s => _envVars[s],
            setUserEnvVar: (name, val) => _envVars[name] = val,
            dnvmFs: new DnvmFs(new MemoryFileSystem())
        );
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
        GlobalOptions.Dispose();
    }
}