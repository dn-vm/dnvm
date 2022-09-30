using System;
using System.IO;

namespace Dnvm.Test;

internal readonly record struct TempDirectory(string Path) : IDisposable
{
    public static TempDirectory CreateSubDirectory(string basePath)
    {
        while (true)
        {
            string dir = System.IO.Path.Combine(basePath, Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(dir);
                return new TempDirectory(dir);
            }
            catch (IOException)
            {
                // retry
            }
        }
    }

    public TempDirectory CreateSubDirectory() => CreateSubDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}