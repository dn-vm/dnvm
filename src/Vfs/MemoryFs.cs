
using System.Collections.Generic;
using System.Threading.Tasks;
using static System.Text.Encoding;

namespace Vfs;

public sealed class MemoryFs : IVfs
{
    private readonly Dictionary<VfsPath, byte[]> _files = new();

    public Task<string> ReadAllTextAsync(VfsPath manifestPath)
    {
        return Task.FromResult(UTF8.GetString(_files[manifestPath]));
    }

    public Task WriteAllText(VfsPath manifest, string text)
    {
        _files[manifest] = UTF8.GetBytes(text);
        return Task.CompletedTask;
    }
}