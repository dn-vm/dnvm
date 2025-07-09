using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zio;

namespace Dnvm;

/// <summary>
/// Provides file-based locking functionality for safe concurrent operations.
/// Uses exclusive file handles to coordinate access across processes.
/// </summary>
public sealed class FileLock : IDisposable
{
    private readonly Stream _lockStream;
    private readonly IFileSystem _fileSystem;
    private readonly UPath _lockFilePath;
    private bool _disposed;

    private FileLock(Stream lockStream, IFileSystem fileSystem, UPath lockFilePath)
    {
        _lockStream = lockStream;
        _fileSystem = fileSystem;
        _lockFilePath = lockFilePath;
    }

    /// <summary>
    /// Attempts to acquire an exclusive file lock at the specified path.
    /// This method is unsafe as it requires the caller to properly dispose the lock.
    /// Use the AcquireAsync methods with a callback function for safer usage.
    /// </summary>
    /// <param name="fileSystem">The file system to use for lock operations</param>
    /// <param name="lockFilePath">Path where the lock file should be created</param>
    /// <param name="timeout">Maximum time to wait for lock acquisition</param>
    /// <param name="baseRetryDelay">Base delay between retry attempts</param>
    /// <param name="cancellationToken">Token to cancel the lock acquisition</param>
    /// <returns>A FileLock instance that must be disposed to release the lock</returns>
    /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired within the timeout period</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is cancelled</exception>
    public static async Task<FileLock> Acquire(
        IFileSystem fileSystem,
        UPath lockFilePath,
        TimeSpan timeout,
        TimeSpan baseRetryDelay)
    {
        return await CancelScope.WithTimeoutAfter(timeout, async cancelScope =>
        {
            int retryCount = 0;
            while (true)
            {
                cancelScope.Token.ThrowIfCancellationRequested();
                try
                {
                    // Ensure the directory exists
                    var lockDir = lockFilePath.GetDirectory();
                    fileSystem.CreateDirectory(lockDir);

                    // Try to create and open the lock file exclusively
                    var lockStream = fileSystem.OpenFile(
                        lockFilePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None);

                    // Write the PID of the process that acquired the lock
                    using (var writer = new StreamWriter(lockStream, leaveOpen: true))
                    {
                        await writer.WriteLineAsync(Environment.ProcessId.ToString());
                        await writer.FlushAsync();
                    }

                    // Reset stream position for potential future reads
                    lockStream.Position = 0;

                    return new FileLock(lockStream, fileSystem, lockFilePath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Lock file is in use by another process - wait and retry
                    var delayMs = Math.Min(
                        baseRetryDelay.Milliseconds * (int)Math.Pow(2, Math.Min(retryCount, 10)),
                        1000);

                    await cancelScope.Delay(TimeSpan.FromMilliseconds(delayMs));
                    retryCount++;
                }
            }
        });
    }

    /// <summary>
    /// Releases the file lock and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _lockStream.Dispose();

            // Try to delete the lock file - best effort cleanup
            try
            {
                if (_fileSystem.FileExists(_lockFilePath))
                {
                    _fileSystem.DeleteFile(_lockFilePath);
                }
            }
            catch (IOException)
            {
                // Ignore cleanup failures - the lock is already released by disposing the stream
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore cleanup failures - the lock is already released by disposing the stream
            }
        }
    }
}
