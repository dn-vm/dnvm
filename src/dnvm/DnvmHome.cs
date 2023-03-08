
using System;
using System.IO;
using System.Threading.Tasks;
using Serde.Json;
using Zio;

namespace Dnvm;

public sealed class DnvmHome
{
    public const string ManifestFileName = "dnvmManifest.json";

    public readonly IFileSystem Vfs;

    public UPath ManifestPath => UPath.Root / ManifestFileName;

    public DnvmHome(IFileSystem vfs)
    {
        Vfs = vfs;
    }

    /// <summary>
    /// Reads a manifest (any version) from the given path and returns
    /// an up-to-date <see cref="Manifest" /> (latest version).
    /// Throws <see cref="InvalidDataException" /> if the manifest is invalid.
    /// </summary>
    public Manifest ReadManifest()
    {
        var text = Vfs.ReadAllText(ManifestPath);
        return ManifestUtils.DeserializeNewOrOldManifest(text) ?? throw new InvalidDataException();
    }

    public void WriteManifest(Manifest manifest)
    {
        var text = JsonSerializer.Serialize(manifest);
        Vfs.WriteAllText(ManifestPath, text);
    }
}