using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using Zio;

namespace Dnvm;

[GenerateSerde]
public sealed partial record WindowsPolicy
{
    public const int CurrentVersion = 1;

    [SerdeMemberOptions(SkipDeserialize = true)]
    public int Version => CurrentVersion;

    public bool PreviewsEnabled { get; init; }
    public EqArray<WindowsTrackedChannel> RegisteredChannels { get; init; } = [];

    public EqArray<WindowsTrackedChannel> TrackedChannels()
        => RegisteredChannels.Where(x => !x.Untracked).ToEq();

    public WindowsPolicy Track(Channel channel)
    {
        var existing = RegisteredChannels.FirstOrDefault(x => x.ChannelName == channel);
        if (existing is null)
        {
            return this with
            {
                RegisteredChannels = RegisteredChannels.Add(new WindowsTrackedChannel
                {
                    ChannelName = channel,
                })
            };
        }

        return existing.Untracked
            ? this with
            {
                RegisteredChannels = RegisteredChannels.Replace(
                    existing,
                    existing with { Untracked = false })
            }
            : this;
    }

    public WindowsPolicy Untrack(Channel channel)
        => this with
        {
            RegisteredChannels = RegisteredChannels
                .Select(x => x.ChannelName == channel ? x with { Untracked = true } : x)
                .ToEq()
        };

    public WindowsPolicy RecordInstallation(Channel channel, SemVersion version)
    {
        var existing = RegisteredChannels.First(x => x.ChannelName == channel);
        if (existing.InstalledSdkVersions.Contains(version))
        {
            return this;
        }
        return this with
        {
            RegisteredChannels = RegisteredChannels.Replace(
                existing,
                existing with
                {
                    InstalledSdkVersions = existing.InstalledSdkVersions.Add(version)
                })
        };
    }

    public WindowsPolicy RemoveVersion(SemVersion version)
        => this with
        {
            RegisteredChannels = RegisteredChannels.Select(channel => channel with
            {
                InstalledSdkVersions = channel.InstalledSdkVersions
                    .Where(x => x != version)
                    .ToEq()
            }).ToEq()
        };
}

[GenerateSerde]
public sealed partial record WindowsTrackedChannel
{
    public required Channel ChannelName { get; init; }

    [SerdeMemberOptions(
        SerializeProxy = typeof(EqArrayProxy.Ser<SemVersion, SemVersionProxy>),
        DeserializeProxy = typeof(EqArrayProxy.De<SemVersion, SemVersionProxy>))]
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = [];

    public bool Untracked { get; init; }
}

public sealed class WindowsPolicyLock : IDisposable
{
    public static readonly UPath PolicyPath = UPath.Root / "dnvmPolicy.json";
    private static readonly UPath LockPath = UPath.Root / "dnvmPolicy.lock";
    private readonly FileLock _fileLock;

    private WindowsPolicyLock(FileLock fileLock)
    {
        _fileLock = fileLock;
    }

    public static async Task<WindowsPolicyLock> Acquire(DnvmEnv env)
        => new(await FileLock.Acquire(
            env.DnvmHomeFs,
            LockPath,
            ManifestLockingConfig.LockTimeout,
            ManifestLockingConfig.BaseRetryDelay));

    public WindowsPolicy ReadOrCreate(DnvmEnv env)
    {
        if (!env.DnvmHomeFs.FileExists(PolicyPath))
        {
            return new WindowsPolicy();
        }
        return JsonSerializer.Deserialize<WindowsPolicy>(
            env.DnvmHomeFs.ReadAllText(PolicyPath));
    }

    public void Write(DnvmEnv env, WindowsPolicy policy)
    {
        var tempPath = UPath.Root / $"dnvmPolicy.{Path.GetRandomFileName()}.tmp";
        env.DnvmHomeFs.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(policy),
            Encoding.UTF8);
        if (env.DnvmHomeFs.FileExists(PolicyPath))
        {
            var backupPath = UPath.Root / "dnvmPolicy.json.backup";
            env.DnvmHomeFs.DeleteFile(backupPath);
            env.DnvmHomeFs.MoveFile(PolicyPath, backupPath);
        }
        env.DnvmHomeFs.MoveFile(tempPath, PolicyPath);
    }

    public void Dispose() => _fileLock.Dispose();
}
