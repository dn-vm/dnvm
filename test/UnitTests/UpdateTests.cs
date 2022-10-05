
using Serde.Json;
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

    // N.B. Hits the public releases endpoint. Could fail if the website is down or internet
    // connectivity is disrupted.
    [Fact]
    public async Task ReleasesEndpointIsUp()
    {
        var responseString = await Program.DefaultClient.GetStringAsync(Update.DefaultReleasesUrl);
        var releases = JsonSerializer.Deserialize<Update.Releases>(responseString);
        Assert.NotEmpty(releases.LatestVersion.Version);
    }
}