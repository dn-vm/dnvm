namespace Dnvm.Test;

internal readonly record struct TempDirectory(string Path) : IDisposable
{
    public static TempDirectory CreateSubDirectory(string basePath)
    {
        string dir = System.IO.Path.Combine(basePath, Guid.NewGuid().ToString());
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
}