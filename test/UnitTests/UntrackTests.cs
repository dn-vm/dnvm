
using System.Collections.Immutable;
using Spectre.Console.Testing;
using Xunit;
using Zio.FileSystems;

namespace Dnvm.Test;

public sealed class UntrackTests
{
    private static Task TestWithServer(Func<MockServer, DnvmEnv, CancellationToken, Task> test)
        => TaskScope.With(async taskScope =>
        {
            await using var mockServer = new MockServer(taskScope);
            using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl);
            await test(mockServer, testOptions.DnvmEnv, taskScope.CancellationToken);
        });

    [Fact]
    public void ChannelUntracked()
    {
        var manifest = Manifest.Empty;
        var logger = new Logger(new TestConsole());
        var result = UntrackCommand.RunHelper(new Channel.Latest(), manifest, logger);
        Assert.True(result is UntrackCommand.Result.ChannelUntracked);
    }

    [Fact]
    public void UntrackLatest()
    {
        var manifest = new Manifest
        {
            TrackedChannels = [new TrackedChannel { ChannelName = new Channel.Latest(), SdkDirName = DnvmEnv.DefaultSdkDirName }]
        };
        var logger = new Logger(new TestConsole());
        var result = UntrackCommand.RunHelper(new Channel.Latest(), manifest, logger);
        if (result is UntrackCommand.Result.Success({} newManifest))
        {
            Assert.True(Assert.Single(newManifest.TrackedChannels).Untracked);
        }
        else
        {
            Assert.True(false, "Expected success");
        }
    }

    [Fact]
    public Task InstallAndUntrack() => TestWithServer(async (mockServer, env, CancellationToken) =>
    {
        using var testOptions = new TestEnv(mockServer.PrefixString, mockServer.DnvmReleasesUrl);
        var logger = new Logger(new TestConsole());
        var result = await TrackCommand.Run(testOptions.DnvmEnv, logger, new CommandArguments.TrackArguments
        {
            Channel = new Channel.Latest(),
            FeedUrl = mockServer.PrefixString
        });
        Assert.Equal(TrackCommand.Result.Success, result);
    });
}