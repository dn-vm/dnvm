
using System;
using System.IO;
using static System.Environment;

namespace Dnvm;

public static class DefaultConfig
{
    public const string FeedUrl = "https://dotnetcli.azureedge.net/dotnet";

    /// <summary>
    /// ~/.local/share/dnvm on Unix systems and %LocalAppData%/dnvm on Windows.
    /// </summary>
    public static readonly string DnvmHome = Path.Combine(
        GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify),
        "dnvm");

    public static string InstallDir => Environment.OSVersion.Platform switch
    {
        PlatformID.Win32NT => WinInstallDir,
        _ => NixInstallDir,
    };

    /// <summary>
    /// Use ~/.local/bin on Unix systems.
    /// </summary>
    public static readonly string NixInstallDir = Path.Combine(
        GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify),
        ".local",
        "bin");

    /// <summary>
    /// Use %LocalAppData%//dnvm on Windows.
    /// </summary>
    public static readonly string WinInstallDir = Path.Combine(
        GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify),
        "dnvm");
}