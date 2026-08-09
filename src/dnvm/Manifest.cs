using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Semver;
using Serde;
using Serde.Json;
using StaticCs.Collections;
using Zio;

namespace Dnvm;

/// <summary>
/// Holds the simple name of a directory that contains one or more SDKs and lives under DNVM_HOME.
/// This is a wrapper to prevent being used directly as a path.
/// </summary>
public sealed record SdkDirName(string Name)
{
    public string Name { get; init; } = Name.ToLower();
}

public partial record RegisteredChannel
{
    public required Channel ChannelName { get; init; }
    public required SdkDirName SdkDirName { get; init; }
    public EqArray<SemVersion> InstalledSdkVersions { get; init; } = EqArray<SemVersion>.Empty;
    public bool Untracked { get; init; } = false;
}

public partial record InstalledSdk
{
    public required SemVersion ReleaseVersion { get; init; }
    public required SemVersion SdkVersion { get; init; }
    public required SemVersion RuntimeVersion { get; init; }
    public required SemVersion AspNetVersion { get; init; }

    public SdkDirName SdkDirName { get; init; } = DnvmEnv.DefaultSdkDirName;
}

public sealed partial record Manifest
{
    public static readonly Manifest Empty = new();

    public SdkDirName CurrentSdkDir { get; init; } = DnvmEnv.DefaultSdkDirName;
    public EqArray<InstalledSdk> InstalledSdks { get; init; } = [];
    public EqArray<RegisteredChannel> RegisteredChannels { get; init; } = [];
}

partial record Manifest
{
    public EqArray<RegisteredChannel> TrackedChannels()
    {
        return RegisteredChannels.Where(x => !x.Untracked).ToEq();
    }

    /// <summary>
    /// Calculates the version of the installed muxer. This is
    /// Max(<all installed _runtime_ versions>).
    /// If no SDKs are installed, returns null.
    /// </summary>
    public SemVersion? MuxerVersion(SdkDirName dir)
    {
        var installedSdks = InstalledSdks
            .Where(s => s.SdkDirName == dir)
            .ToList();
        if (installedSdks.Count == 0)
        {
            return null;
        }
        return installedSdks
            .Select(s => s.RuntimeVersion)
            .Max(SemVersion.SortOrderComparer);
    }

    public Manifest AddSdk(
        SemVersion semVersion,
        Channel? c = null,
        SdkDirName? sdkDirParam = null)
    {
        if (sdkDirParam is not { } sdkDir)
        {
            sdkDir = DnvmEnv.DefaultSdkDirName;
        }
        var installedSdk = new InstalledSdk()
        {
            SdkDirName = sdkDir,
            SdkVersion = semVersion,
            RuntimeVersion = semVersion,
            AspNetVersion = semVersion,
            ReleaseVersion = semVersion,
        };
        return AddSdk(installedSdk, c);
    }

    public Manifest AddSdk(InstalledSdk sdk, Channel? c = null)
    {
        var installedSdks = this.InstalledSdks;
        if (!installedSdks.Contains(sdk))
        {
            installedSdks = installedSdks.Add(sdk);
        }
        EqArray<RegisteredChannel> allChannels = this.RegisteredChannels;
        if (allChannels.FirstOrNull(x => !x.Untracked && x.ChannelName == c && x.SdkDirName == sdk.SdkDirName) is { } oldTracked)
        {
            var installedSdkVersions = oldTracked.InstalledSdkVersions;
            var newTracked = installedSdkVersions.Contains(sdk.SdkVersion)
                ? oldTracked
                : oldTracked with
                {
                    InstalledSdkVersions = installedSdkVersions.Add(sdk.SdkVersion)
                };
            allChannels = allChannels.Replace(oldTracked, newTracked);
        }
        else if (c is not null)
        {
            allChannels = allChannels.Add(new RegisteredChannel
            {
                ChannelName = c,
                SdkDirName = sdk.SdkDirName,
                InstalledSdkVersions = [sdk.SdkVersion]
            });
        }
        return this with
        {
            InstalledSdks = installedSdks,
            RegisteredChannels = allChannels,
        };
    }

    public bool IsSdkInstalled(SemVersion version, SdkDirName dirName)
    {
        return this.InstalledSdks.Any(s => s.SdkVersion == version && s.SdkDirName == dirName);
    }

    public Manifest TrackChannel(RegisteredChannel channel)
    {
        var existing = RegisteredChannels.FirstOrNull(c =>
            c.ChannelName == channel.ChannelName && c.SdkDirName == channel.SdkDirName);
        if (existing is null)
        {
            return this with
            {
                RegisteredChannels = RegisteredChannels.Add(channel)
            };
        }
        else if (existing is { Untracked: true })
        {
            var newVersions = existing.InstalledSdkVersions.Concat(channel.InstalledSdkVersions).Distinct().ToEq();
            return this with
            {
                RegisteredChannels = RegisteredChannels.Replace(existing, existing with
                {
                    InstalledSdkVersions = newVersions,
                    Untracked = false,
                })
            };
        }
        throw new InvalidOperationException("Channel already tracked");
    }

    internal Manifest UntrackChannel(Channel channel)
    {
        return this with
        {
            RegisteredChannels = RegisteredChannels.Select(c =>
            {
                if (c.ChannelName == channel)
                {
                    return c with { Untracked = true };
                }
                return c;
            }).ToEq()
        };
    }
}

public sealed class ManifestLock : IDisposable
{
    private readonly FileLock _fileLock;
    private bool _disposed;

    private ManifestLock(FileLock fileLock)
    {
        _fileLock = fileLock;
    }

    public static async Task<ManifestLock> Acquire(DnvmEnv env)
    {
        var fileLock = await FileLock.Acquire(
            env.DnvmHomeFs,
            DnvmEnv.LockFilePath,
            ManifestLockingConfig.LockTimeout,
            ManifestLockingConfig.BaseRetryDelay);
        return new ManifestLock(fileLock);
    }

    public void Dispose()
    {
        _fileLock.Dispose();
        _disposed = true;
    }

    public async Task<Manifest> ReadManifest(DnvmEnv env)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ManifestLock));
        }
        return await Manifest.ReadManifestUnsafe(env);
    }

    /// <summary>
    /// Writes the manifest atomically using a temporary file and rename operation.
    /// </summary>
    public async Task WriteManifest(DnvmEnv env, Manifest manifest)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ManifestLock));
        }
        await Manifest.WriteManifestUnsafe(env, manifest);
    }

    /// <summary>
    /// Read a manifest using <see cref="ReadManifestUnsafe"/> , or create a new empty manifest if the
    /// manifest file does not exist.
    /// </summary>
    public async Task<Manifest> ReadOrCreateManifest(DnvmEnv env)
    {
        try
        {
            return await ReadManifest(env);
        }
        // Not found is expected
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException) { }

        return Manifest.Empty;
    }
}

/// <summary>
/// Static methods for atomic manifest file operations with locking.
/// </summary>
partial record Manifest
{
    /// <summary>
    /// Reads a manifest (any version) from the given path without acquiring the lock and returns an
    /// up-to-date <see cref="Manifest" /> (latest version). Throws if the manifest is invalid.
    /// </summary>
    public static async Task<Manifest> ReadManifestUnsafe(DnvmEnv env)
    {
        var text = env.DnvmHomeFs.ReadAllText(DnvmEnv.ManifestPath);
        return await ManifestSerialize.DeserializeNewOrOldManifest(env.HttpClient, text, env.DotnetFeedUrls);
    }

    /// <summary>
    /// Writes a manifest to the file system without locking. This method does not acquire a lock,
    /// and is therefore not safe for concurrent writes. For safe concurrent writes, use
    /// UpdateManifestAtomic instead.
    /// </summary>
    public static Task WriteManifestUnsafe(DnvmEnv env, Manifest manifest)
    {
        var tempFileName = $"{DnvmEnv.ManifestFileName}.{Path.GetRandomFileName()}.tmp";
        var tempPath = UPath.Root / tempFileName;

        var text = JsonSerializer.Serialize(manifest.ConvertToLatest());

        // Write to temporary file first
        env.DnvmHomeFs.WriteAllText(tempPath, text, Encoding.UTF8);

        // Create backup of existing manifest if it exists
        if (env.DnvmHomeFs.FileExists(DnvmEnv.ManifestPath))
        {
            var backupPath = UPath.Root / $"{DnvmEnv.ManifestFileName}.backup";
            try
            {
                // Should not throw if the file doesn't exist
                env.DnvmHomeFs.DeleteFile(backupPath);
                env.DnvmHomeFs.MoveFile(DnvmEnv.ManifestPath, backupPath);
            }
            catch (IOException)
            {
                // Best effort cleanup - ignore if we can't delete the backup file
            }
        }

        // Atomic rename operation
        env.DnvmHomeFs.MoveFile(tempPath, DnvmEnv.ManifestPath);

        // Clean up temporary file
        try
        {
            env.DnvmHomeFs.DeleteFile(tempPath);
        }
        catch (IOException)
        {
            // Best effort cleanup - ignore if we can't delete the temp file
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Configuration for manifest locking behavior.
/// </summary>
internal static class ManifestLockingConfig
{
    /// <summary>
    /// Maximum time to wait for acquiring a manifest lock before timing out.
    /// </summary>
    public static TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of retry attempts for transient failures.
    /// </summary>
    public static int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay between retry attempts (with jitter added).
    /// </summary>
    public static TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);
}

