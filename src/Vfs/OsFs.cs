
using System.IO;
using System.Threading.Tasks;

namespace Vfs;

public sealed class OsFs : IVfs
{
    private readonly string _basePath;
    public OsFs(string basePath)
    {
        _basePath = basePath;
    }

    private string GetOsPath(VfsPath vfsPath) => Path.Combine(_basePath, vfsPath.ToString()[1..]);

    public Task<string> ReadAllTextAsync(VfsPath path)
    {
        return File.ReadAllTextAsync(GetOsPath(path));
    }

    public async Task WriteAllText(VfsPath path, string text)
    {
        await File.WriteAllTextAsync(GetOsPath(path), text);
    }
}