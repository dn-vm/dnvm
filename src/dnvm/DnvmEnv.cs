
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Serde.Json;
using Zio;
using Zio.FileSystems;
using static System.Environment;

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
    /// Default DNVM_HOME is
    ///  ~/.local/share/dnvm on Linux
    ///  %LocalAppData%/dnvm on Windows
    ///  ~/Library/Application Support/dnvm on Mac
    /// </summary>
    public static readonly string DefaultDnvmHome = Path.Combine(
        GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify),
        "dnvm");

    /// <summary>
    /// The location of the SDK install directory, relative to <see cref="DnvmHome" />
    /// </summary>
    public static readonly SdkDirName DefaultSdkDirName = new("dn");


    /// <summary>
    /// Get the path to DNVM_HOME, which is the location of the dnvm manifest
    /// and the installed SDKs. If the environment variable is not set, uses
    /// <see cref="DnvmEnv.DefaultDnvmHome" /> as the default.
    /// </summar>
    public static DnvmEnv CreateDefault(string? home = null)
    {
        home ??= Environment.GetEnvironmentVariable("DNVM_HOME");
        var dnvmHome = string.IsNullOrWhiteSpace(home)
            ? DefaultDnvmHome
            : home;
        return CreatePhysical(dnvmHome,
            n => Environment.GetEnvironmentVariable(n, EnvironmentVariableTarget.User),
            (n, v) => Environment.SetEnvironmentVariable(n, v, EnvironmentVariableTarget.User));
    }

    public static DnvmEnv CreatePhysical(
        string realPath,
        Func<string, string?> getUserEnvVar,
        Action<string, string> setUserEnvVar)
    {
        Directory.CreateDirectory(realPath);
        var physicalFs = new PhysicalFileSystem();
        return new DnvmEnv(
            userHome: GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify),
            new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(realPath)),
            isPhysical: true,
            getUserEnvVar,
            setUserEnvVar);
    }

    public bool IsPhysicalDnvmHome { get; }
    public readonly IFileSystem Vfs;
    public string RealPath(UPath path) => Vfs.ConvertPathToInternal(path);
    public SubFileSystem TempFs { get; }
    public Func<string, string?> GetUserEnvVar { get; }
    public Action<string, string> SetUserEnvVar { get; }
    public string DotnetFeedUrl { get; }
    public string DnvmReleasesUrl { get; }
    public string UserHome { get; }


    public DnvmEnv(
        string userHome,
        IFileSystem vfs,
        bool isPhysical,
        Func<string, string?> getUserEnvVar,
        Action<string, string> setUserEnvVar,
        string dotnetFeedUrl = DnvmEnv.DefaultDotnetFeedUrl,
        string releasesUrl = DnvmEnv.DefaultReleasesUrl)
    {
        UserHome = userHome;
        Vfs = vfs;
        IsPhysicalDnvmHome = isPhysical;
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
    public async Task<Manifest> ReadManifest()
    {
        var text = Vfs.ReadAllText(ManifestPath);
        return (await ManifestUtils.DeserializeNewOrOldManifest(text, DotnetFeedUrl)) ?? throw new InvalidDataException();
    }

    public void WriteManifest(Manifest manifest)
    {
        var text = JsonSerializer.Serialize(manifest);
        Vfs.WriteAllText(ManifestPath, text, Encoding.UTF8);
    }

    public void Dispose()
    {
        Vfs.Dispose();
        TempFs.Dispose();
    }
}