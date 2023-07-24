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
    public const string DotnetFeedUrl = "https://dotnetcli.azureedge.net/dotnet";

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

    public static readonly GlobalOptions Default = new() {
        UserHome = GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify),
        DnvmHome = DefaultDnvmHome,
        GetUserEnvVar = s => GetEnvironmentVariable(s, EnvironmentVariableTarget.User),
        SetUserEnvVar = (name, val) => Environment.SetEnvironmentVariable(name, val, EnvironmentVariableTarget.User),
        DnvmFs = DnvmFs.CreatePhysical(DefaultDnvmHome),
    };

    public required string UserHome { get; init; }
    public required string DnvmHome { get; init; }
    public required Func<string, string?> GetUserEnvVar { get; init; }
    public required Action<string, string> SetUserEnvVar { get; init; }
    public required DnvmFs DnvmFs { get; init; }

    private readonly string? _dnvmInstallPath;
    public string DnvmInstallPath { get => _dnvmInstallPath ?? DnvmHome; init => _dnvmInstallPath = value; }

    public string ManifestPath => Path.Combine(DnvmHome, ManifestFileName);

    public void Dispose()
    {
        DnvmFs.Dispose();
    }
}