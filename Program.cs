
using System.Diagnostics;
using System.Runtime.InteropServices;
using Internal.CommandLine;
using static System.Environment;

namespace Dnvm;

public class Program
{
    private static readonly HttpClient s_client = new HttpClient();

    static async Task<int> Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        var feeds = new[] {
            "https://dotnetcli.azureedge.net/dotnet",
            "https://dotnetbuilds.azureedge.net/public"
        };

        string feed = feeds[0];

        string? osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ?
                RuntimeInformation.RuntimeIdentifier.Contains("musl") ? "linux-musl"
                : "linux"
            : null;

        if (osName is null)
        {
            Console.WriteLine("Could not determine current OS");
            return 1;
        }

        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
        string suffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "zip"
            : "tar.gz";

        string latestVersion = await GetLatestVersion(feed, options.Channel);
        Console.WriteLine(latestVersion);
        string archiveName = ConstructArchiveName(latestVersion, osName, arch, suffix);
        Console.WriteLine(archiveName);
        string link = ConstructDownloadLink(feed, latestVersion, archiveName);
        Console.WriteLine(link);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        Console.WriteLine(archivePath);

        using (var tempArchiveFile = File.Create(archivePath))
        using (var archiveHttpStream = await s_client.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
        }

        string installDir = Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), ".dotnet");
        Console.WriteLine(installDir);
        await ExtractArchiveToDir(archivePath, installDir);

        return 0;
    }

    static async Task ExtractArchiveToDir(string archivePath, string dirPath)
    {
        Directory.CreateDirectory(dirPath);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var psi = new ProcessStartInfo()
            {
                FileName = "tar",
                ArgumentList = { "-xzf", $"{archivePath}", "-C", $"{dirPath}" },
            };

            var p = Process.Start(psi);
            if (p is not null)
            {
                await p.WaitForExitAsync();
            }
        }
    }

    static string ConstructArchiveName(
        string specificVersion,
        string osName,
        string arch,
        string suffix)
    {
        return $"dotnet-sdk-{specificVersion}-{osName}-{arch}.{suffix}";
    }

    static string ConstructDownloadLink(
        string feed,
        string specificVersion,
        string archiveName)
    {
        return $"{feed}/Sdk/{specificVersion}/{archiveName}";
    }

    static async Task<string> GetLatestVersion(string feed, Channel channel)
    {
        string versionFileUrl = $"{feed}/Sdk/{channel.ToString()}/latest.version";
        var body = await s_client.GetStringAsync(versionFileUrl);
        return body;
    }
}