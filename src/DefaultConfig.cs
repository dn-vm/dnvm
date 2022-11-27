
using System.IO;
using System.Runtime.InteropServices;
using static System.Environment;

namespace Dnvm;

public static class DefaultConfig
{
    public const string FeedUrl = "https://dotnetcli.azureedge.net/dotnet";

    internal static readonly string InstallDir = Path.Combine(GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify), "dnvm");
    internal static readonly string GlobalInstallDir =
        Utilities.CurrentRID.OS == OSPlatform.Windows ? Path.Combine(GetFolderPath(SpecialFolder.ProgramFiles), "dotnet")
        : Utilities.CurrentRID.OS == OSPlatform.OSX ? "/usr/local/share/dotnet" // MacOS no longer lets anyone mess with /usr/share, even as root
        : "/usr/share/dotnet";
}