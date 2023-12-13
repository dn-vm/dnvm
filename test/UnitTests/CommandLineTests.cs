
using Xunit;

namespace Dnvm.Test;

public class CommandLineTests
{
    [Fact]
    public void TrackMajorMinor()
    {
        var options = CommandLineArguments.Parse([
            "track",
            "99.99"
        ]);
        Assert.True(options.Command is CommandArguments.TrackArguments {
            Channel: Channel.Versioned { Major: 99, Minor: 99 }
        });
    }
}