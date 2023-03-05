
using Serde.Json;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class ListTests : IDisposable
{
    private readonly Logger _logger;
    private readonly TempDirectory _userHome = TestUtils.CreateTempDirectory();
    private readonly TempDirectory _dnvmHome = TestUtils.CreateTempDirectory();
    private readonly Dictionary<string, string> _envVars = new();
    private readonly GlobalOptions _globalOptions;

    public ListTests(ITestOutputHelper output)
    {
        var wrapper = new OutputWrapper(output);
        _logger = new Logger(wrapper, wrapper);
        _globalOptions = new GlobalOptions {
            DnvmHome = _dnvmHome.Path,
            UserHome = _userHome.Path,
            GetUserEnvVar = s => _envVars[s],
            SetUserEnvVar = (name, val) => _envVars[name] = val,
        };
    }

    public void Dispose()
    {
        _userHome.Dispose();
        _dnvmHome.Dispose();
    }

    [Fact]
    public void BasicList()
    {
        var writer = new StringWriter();
        var logger = new Logger(writer, writer);

        var manifest = Manifest.Empty
            .AddSdk(new InstalledSdk("1.0.0"), Channel.Latest)
            .AddSdk(new InstalledSdk("4.0.0-preview1") { SdkDirName = new("preview") }, Channel.Preview);

        ListCommand.PrintSdks(logger, manifest);
        var output = """
Installed SDKs:

  | Channel	Version	Location
----------------------------
* | Latest	1.0.0	dn
  | Preview	4.0.0-preview1	preview

""";

        Assert.Equal(output, writer.ToString());
    }

    [Fact]
    public void ListFromFile()
    {
        var writer = new StringWriter();
        var logger = new Logger(writer, writer);

        var manifest = Manifest.Empty
            .AddSdk(new InstalledSdk("42.42.42"), Channel.Latest);

        File.WriteAllText(_globalOptions.ManifestPath, JsonSerializer.Serialize(manifest));

        ListCommand.Run(logger, _globalOptions);
        var output = """
Installed SDKs:

  | Channel	Version	Location
----------------------------
* | Latest	42.42.42	dn

""";

        Assert.Equal(output, writer.ToString());
    }
}