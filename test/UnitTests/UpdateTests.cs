
using Xunit;

namespace Dnvm.Test;

public class UpdateTests
{
    [Fact]
    public async Task CheckUrl()
    {
        using var mockServer = new MockServer();
        var update = new Dnvm.Update(new Logger(), new Command.UpdateOptions {
            ReleasesUrl = $"http://localhost:{mockServer.Port}/releases.json"
        });
        var link = await update.GetReleaseLink();
        Assert.Equal($"{mockServer.PrefixString}dnvm/dnvm.{Utilities.ZipSuffix}", link);
    }
}