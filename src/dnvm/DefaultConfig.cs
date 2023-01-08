
using System.IO;
using static System.Environment;

namespace Dnvm;

public static class DefaultConfig
{
    public const string FeedUrl = "https://dotnetcli.azureedge.net/dotnet";

    public static readonly string InstallDir = Path.Combine(
        GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify),
        "dnvm");
}