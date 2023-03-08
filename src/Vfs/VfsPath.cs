
using System;
using System.Collections;
using System.Collections.Immutable;
using System.Linq;

namespace Vfs;

public sealed class VfsPath : IEquatable<VfsPath>
{
    // VFS paths are unique to a given IVfs instance.
    private readonly IVfs _vfs;

    private readonly ImmutableArray<string> _parts;

    /// <summary>
    /// Create a new root VFS path.
    /// </summary>
    public VfsPath(IVfs vfs)
    {
        _vfs = vfs;
        _parts = ImmutableArray<string>.Empty;
    }

    private VfsPath(IVfs vfs, ImmutableArray<string> parts)
    {
        _vfs = vfs;
        _parts = parts;
    }

    /// <summary>
    /// Create a new VFS path relative to this path. The incoming path must be relative.  Resolves
    /// "." and ".." parts. If the resolved path would be outside the root, the root is returned
    /// instead.
    /// </summary>
    public VfsPath Combine(string relativePath)
    {
        if (relativePath.StartsWith('/'))
        {
            throw new ArgumentException("Path must be relative", nameof(relativePath));
        }

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var builder = ImmutableArray.CreateBuilder<string>();
        builder.AddRange(_parts);

        // Add parts while handling "." and ".."
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (builder.Count > 0)
                {
                    builder.RemoveAt(builder.Count - 1);
                }
                continue;
            }
            builder.Add(part);
        }

        return new VfsPath(this._vfs, builder.ToImmutable());
    }

    public override string ToString() => '/' + string.Join('/', _parts);

    public override bool Equals(object? obj) => Equals(obj as VfsPath);

    public bool Equals(VfsPath? other)
    {
        return other is not null
            && object.ReferenceEquals(this._vfs, other._vfs)
            && this._parts.SequenceEqual(other._parts);
    }

    public override int GetHashCode()
    {
        int code = 0;
        code = HashCode.Combine(code, _vfs);
        foreach (var item in _parts)
        {
            code = HashCode.Combine(code, item);
        }
        return code;
    }
}