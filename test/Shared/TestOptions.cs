
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
        GlobalOptions = new GlobalOptions {
            DnvmHome = _dnvmHome.Path,
            UserHome = _userHome.Path,
            GetUserEnvVar = s => _envVars[s],
            SetUserEnvVar = (name, val) => _envVars[name] = val,
            DnvmFs = new DnvmFs(new MemoryFileSystem())
        };
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
    }
}