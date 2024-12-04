using IOPath = System.IO.Path;

namespace Dnvm.Test;

public sealed record TempDirectory(string Path) : IDisposable
{
    public static TempDirectory CreateSubDirectory(string basePath)
    {
        string dir = IOPath.Combine(basePath, IOPath.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return new TempDirectory(dir);
    }

    public TempDirectory CreateSubDirectory() => CreateSubDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Copy a file from another path into this directory.
    /// </summary>
    public string CopyFile(string path)
    {
        var newPath = IOPath.Combine(Path, IOPath.GetFileName(Path));
        File.Copy(path, newPath);
        return newPath;
    }
}