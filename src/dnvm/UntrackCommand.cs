using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;

namespace Dnvm;

public sealed class UntrackCommand
{
    public abstract record Result
    {
        private Result() { }


        public sealed record Success(Manifest Manifest) : Result;
        public sealed record ChannelUntracked : Result;
        public record ManifestReadError : Result;
    }

    public static async Task<int> Run(DnvmEnv env, Channel channel)
    {
        using var @lock = await ManifestLock.Acquire(env);
        Manifest manifest;
        try
        {
            manifest = await @lock.ReadManifest(env);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            env.Console.Error("Failed to read manifest file");
            return 1;
        }
        var result = RunHelper(channel, manifest, env.Console);
        if (result is Result.Success({} newManifest))
        {
            await @lock.WriteManifest(env, newManifest);
            return 0;
        }
        return 1;
    }

    public static Result RunHelper(Channel channel, Manifest manifest, IAnsiConsole console)
    {
        if (!manifest.TrackedChannels().Any(c => c.ChannelName == channel))
        {
            console.WriteLine("Channel '{channel}' is not tracked");
            return new Result.ChannelUntracked();
        }

        return new Result.Success(manifest.UntrackChannel(channel));
    }
}