using System;
using System.IO;
using static System.Environment;

namespace Dnvm;

/// <summary>
/// GlobalConfig contains options used by all of dnvm, like the DNVM_HOME path,
/// the SDK install path, and the location of the user's home directory.
/// </summary>
public sealed class GlobalOptions : IDisposable
{
    public const string DefaultDotnetFeedUrl = "https://dotnetcli.azureedge.net/dotnet";
    public const string DefaultReleasesUrl = "https://github.com/dn-vm/dn-vm.github.io/raw/gh-pages/releases.json";

    /// <summary>
    /// Default DNVM_HOME is
    ///  ~/.local/share/dnvm on Linux
    ///  %LocalAppData%/dnvm on Windows
    ///  ~/Library/Application Support/dnvm on Mac
    /// </summary>
    public static readonly string DefaultDnvmHome = Path.Combine(
        GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify),
        "dnvm");

    public const string ManifestFileName = "dnvmManifest.json";

    /// <summary>
    /// The location of the SDK install directory, relative to <see cref="DnvmHome" />
    /// </summary>
    public static readonly SdkDirName DefaultSdkDirName = new("dn");


    public string UserHome { get; }
    public string DnvmHome { get; }
    public Func<string, string?> GetUserEnvVar { get; }
    public Action<string, string> SetUserEnvVar { get; }
    public DnvmFs DnvmFs { get; }
    public string DotnetFeedUrl { get; init; }
    public string DnvmReleasesUrl { get; init; }

    public GlobalOptions(
        string userHome,
        string dnvmHome,
        Func<string, string?> getUserEnvVar,
        Action<string, string> setUserEnvVar,
        DnvmFs dnvmFs,
        string dotnetFeedUrl = DefaultDotnetFeedUrl,
        string releasesUrl = DefaultReleasesUrl)
    {
        UserHome = userHome;
        DnvmHome = dnvmHome;
        GetUserEnvVar = getUserEnvVar;
        SetUserEnvVar = setUserEnvVar;
        DnvmFs = dnvmFs;
        DotnetFeedUrl = dotnetFeedUrl;
        DnvmReleasesUrl = releasesUrl;
    }

    private readonly string? _dnvmInstallPath;
    public string DnvmInstallPath { get => _dnvmInstallPath ?? DnvmHome; init => _dnvmInstallPath = value; }

    public string ManifestPath => Path.Combine(DnvmHome, ManifestFileName);

    public void Dispose()
    {
        DnvmFs.Dispose();
    }
}