
using System.Collections.Immutable;
using Semver;
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
            RegisteredChannels = [new RegisteredChannel { ChannelName = new Channel.Latest(), SdkDirName = DnvmEnv.DefaultSdkDirName }]
        };
        var logger = new Logger(new TestConsole());
        var result = UntrackCommand.RunHelper(new Channel.Latest(), manifest, logger);
        if (result is UntrackCommand.Result.Success({} newManifest))
        {
            Assert.True(Assert.Single(newManifest.RegisteredChannels).Untracked);
        }
        else
        {
            Assert.Fail();
        }
    }

    [Fact]
    public Task InstallAndUntrack() => TestWithServer(async (mockServer, env, CancellationToken) =>
    {
        var logger = new Logger(new TestConsole());
        var channel = new Channel.Latest();
        var result = await TrackCommand.Run(env, logger, new TrackCommand.Options
        {
            Channel = channel,
            FeedUrl = mockServer.PrefixString
        });
        Assert.Equal(TrackCommand.Result.Success, result);

        var manifest = await env.ReadManifest();
        Assert.Equal(manifest.TrackedChannels(), [new RegisteredChannel {
            ChannelName = channel,
            InstalledSdkVersions = [ MockServer.DefaultLtsVersion ],
            SdkDirName = DnvmEnv.DefaultSdkDirName }]);
        var untrackCode = await UntrackCommand.Run(env, logger, channel);
        Assert.Equal(0, untrackCode);
    });

    [Fact]
    public Task DoubleUntrack() => TestWithServer(async (mockServer, env, cancellationToken) =>
    {
        var logger = new Logger(new TestConsole());
        var channel = new Channel.Latest();
        var result = await TrackCommand.Run(env, logger, new TrackCommand.Options
        {
            Channel = channel,
            FeedUrl = mockServer.PrefixString
        });
        Assert.Equal(TrackCommand.Result.Success, result);

        var untrackCode = await UntrackCommand.Run(env, logger, channel);
        Assert.Equal(0, untrackCode);
        var manifest = await env.ReadManifest();
        var untrackResult = UntrackCommand.RunHelper(channel, manifest, logger);
        Assert.True(untrackResult is UntrackCommand.Result.ChannelUntracked);
    });
}