
using System.Threading.Tasks;

namespace Vfs;

public interface IVfs
{
    Task<string> ReadAllTextAsync(VfsPath manifestPath);
    Task WriteAllText(VfsPath manifest, string text);
}