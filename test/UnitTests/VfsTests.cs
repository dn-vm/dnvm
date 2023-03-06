
using Microsoft.VisualBasic;
using Xunit;

namespace Vfs;

public sealed class VfsTests
{
    [Fact]
    public void DotDotCantGoAboveRoot()
    {
        var path = new VfsPath(ThrowingVfs.Instance);
        Assert.Equal("/", path.ToString());
        path = path.Combine("..");
        Assert.Equal("/", path.ToString());
    }

    /// <summary>
    /// A VFS that throws on all operations.
    /// </summary>
    private sealed class ThrowingVfs : IVfs
    {
        public static readonly ThrowingVfs Instance = new();
        public Task<string> ReadAllTextAsync(VfsPath manifestPath) => throw new NotImplementedException();
        public Task WriteAllText(VfsPath manifest, string text) => throw new NotImplementedException();
    }
}