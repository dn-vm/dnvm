
using System;
using System.IO;
using System.Threading.Tasks;
using Serde.Json;
using Zio;
using Zio.FileSystems;

namespace Dnvm;

/// <summary>
/// Represents the environment of a dnvm process.
/// <summary>
public sealed class DnvmEnv : IDisposable
{
    public const string ManifestFileName = "dnvmManifest.json";
    public const string DefaultDotnetFeedUrl = "https://dotnetcli.azureedge.net/dotnet";
    public const string DefaultReleasesUrl = "https://github.com/dn-vm/dn-vm.github.io/raw/gh-pages/releases.json";
    public static UPath ManifestPath => UPath.Root / ManifestFileName;
    public static UPath EnvPath => UPath.Root / "env";
    public static UPath DnvmExePath => UPath.Root / Utilities.DnvmExeName;
    public static UPath SymlinkPath => UPath.Root / Utilities.DotnetSymlinkName;
    public static UPath GetSdkPath(SdkDirName sdkDirName) => UPath.Root / sdkDirName.Name;

    /// <summary>
    /// Get the path to DNVM_HOME, which is the location of the dnvm manifest
    /// and the installed SDKs. If the environment variable is not set, uses
    /// <see cref="GlobalOptions.DefaultDnvmHome" /> as the default.
    /// </summar>
    public static DnvmEnv CreateDefault()
    {
        var home = Environment.GetEnvironmentVariable("DNVM_HOME");
        var dnvmHome = string.IsNullOrWhiteSpace(home)
            ? GlobalOptions.DefaultDnvmHome
            : home;
        return CreatePhysical(dnvmHome);
    }

    public static DnvmEnv CreatePhysical(string realPath)
    {
        Directory.CreateDirectory(realPath);
        var physicalFs = new PhysicalFileSystem();
        return new DnvmEnv(
            new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(realPath)),
            Environment.GetEnvironmentVariable,
            Environment.SetEnvironmentVariable);
    }

    public readonly IFileSystem Vfs;
    public string RealPath(UPath path) => Vfs.ConvertPathToInternal(path);
    public SubFileSystem TempFs { get; }
    public Func<string, string?> GetUserEnvVar { get; }
    public Action<string, string> SetUserEnvVar { get; }
    public string DotnetFeedUrl { get; }
    public string DnvmReleasesUrl { get; }

    public DnvmEnv(
        IFileSystem vfs,
        Func<string, string?> getUserEnvVar,
        Action<string, string> setUserEnvVar,
        string dotnetFeedUrl = DnvmEnv.DefaultDotnetFeedUrl,
        string releasesUrl = DnvmEnv.DefaultReleasesUrl)
    {
        Vfs = vfs;
        // TempFs must be a physical file system because we pass the path to external
        // commands that will not be able to write to shared memory
        var physical = new PhysicalFileSystem();
        TempFs = new SubFileSystem(
            physical,
            physical.ConvertPathFromInternal(Path.GetTempPath()),
            owned: true);
        GetUserEnvVar = getUserEnvVar;
        SetUserEnvVar = setUserEnvVar;
        DotnetFeedUrl = dotnetFeedUrl;
        DnvmReleasesUrl = releasesUrl;
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
        var tmpPath = ManifestPath + ".tmp";
        Vfs.WriteAllText(tmpPath, text);
        Vfs.MoveFile(tmpPath, ManifestPath);
    }

    public void Dispose()
    {
        Vfs.Dispose();
        TempFs.Dispose();
    }
}