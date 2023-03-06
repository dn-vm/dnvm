
using System;
using System.IO;
using System.Threading.Tasks;
using Serde.Json;
using Vfs;

namespace Dnvm;

public sealed class DnvmHome
{
    public const string ManifestFileName = "dnvmManifest.json";

    public readonly IVfs Vfs;
    private readonly VfsPath _root;

    public VfsPath ManifestPath => _root.Combine(ManifestFileName);

    public DnvmHome(IVfs vfs)
    {
        Vfs = vfs;
        _root = new VfsPath(vfs);
    }

    /// <summary>
    /// Reads a manifest (any version) from the given path and returns
    /// an up-to-date <see cref="Manifest" /> (latest version).
    /// Throws <see cref="InvalidDataException" /> if the manifest is invalid.
    /// </summary>
    public async Task<Manifest> ReadManifest()
    {
        var text = await Vfs.ReadAllTextAsync(ManifestPath);
        return ManifestUtils.DeserializeNewOrOldManifest(text) ?? throw new InvalidDataException();
    }

    public Task WriteManifest(Manifest manifest)
    {
        var text = JsonSerializer.Serialize(manifest);
        return Vfs.WriteAllText(ManifestPath, text);
    }
}