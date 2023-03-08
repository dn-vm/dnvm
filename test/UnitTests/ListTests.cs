
using Serde.Json;
using Vfs;
using Xunit;
using Xunit.Abstractions;

namespace Dnvm.Test;

public sealed class ListTests
{
    private readonly StringWriter _writer = new();
    private readonly Logger _logger;

    public ListTests()
    {
        _logger = new Logger(_writer, _writer);
    }

    [Fact]
    public void BasicList()
    {
        var manifest = Manifest.Empty
            .AddSdk(new InstalledSdk("1.0.0"), Channel.Latest)
            .AddSdk(new InstalledSdk("4.0.0-preview1") { SdkDirName = new("preview") }, Channel.Preview);

        ListCommand.PrintSdks(_logger, manifest);
        var output = """
Installed SDKs:

  | Channel	Version	Location
----------------------------
* | Latest	1.0.0	dn
  | Preview	4.0.0-preview1	preview

""";

        Assert.Equal(output, _writer.ToString());
    }

    [Fact]
    public async Task ListFromFile()
    {
        var manifest = Manifest.Empty
            .AddSdk(new InstalledSdk("42.42.42"), Channel.Latest);

        var home = new DnvmHome(new MemoryFs());
        await home.WriteManifest(manifest);

        var ret = await ListCommand.Run(_logger, home);
        Assert.Equal(0, ret);
        var output = """
Installed SDKs:

  | Channel	Version	Location
----------------------------
* | Latest	42.42.42	dn

""";

        Assert.Equal(output, _writer.ToString());
    }
}