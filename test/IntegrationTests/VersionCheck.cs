
using Serde.Json;
using Xunit;

namespace Dnvm.Test;

public class VersionCheck
{
    // N.B. Hits the public releases endpoint. Could fail if the website is down or internet
    // connectivity is disrupted.
    [Fact]
    public async Task ReleasesEndpointIsUp()
    {
        var releasesIndex = await DotnetReleasesIndex.FetchLatestIndex(GlobalOptions.DotnetFeedUrl);
        Assert.NotEmpty(releasesIndex.Releases);
    }
}