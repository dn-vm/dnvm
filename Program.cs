
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

        string link = await ConstructDownloadLink(feed, options.Channel, osName, arch, suffix);
        Console.WriteLine(link);
        string archivePath = Path.Combine(Path.GetTempPath(), ConstructArchiveName(null, osName, arch, suffix));
        Console.WriteLine(archivePath);

        using (var tempArchiveFile = File.Create(archivePath))
        using (var archiveHttpStream = await s_client.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
        }

        string installDir = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".dotnet");
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
        string? specificVersion,
        string osName,
        string arch,
        string suffix)
    {
        return specificVersion is null
            ? $"dotnet-sdk-{osName}-{arch}.{suffix}"
            : $"dotnet-sdk-{specificVersion}-{osName}-{arch}.{suffix}";
    }

    static async Task<string> ConstructDownloadLink(
        string feed,
        Channel channel,
        string osName,
        string arch,
        string suffix)
    {
        // The dotnet service provides an endpoint for fetching the latest LTS and Current versions,
        // but not preview. We'll have to construct that ourselves.
        if (channel != Channel.Preview)
        {
            string latestVersion = await GetLatestVersion(feed, channel);
            Console.WriteLine(latestVersion);
            var archiveName = ConstructArchiveName(latestVersion, osName, arch, suffix);
            return $"{feed}/Sdk/{latestVersion}/{archiveName}";
        }
        else
        {
            const string PreviewMajorVersion = "7.0";
            var archiveName = ConstructArchiveName(null, osName, arch, suffix);
            return $"https://aka.ms/dotnet/{PreviewMajorVersion}/preview/{archiveName}";
        }
    }

    static async Task<string> GetLatestVersion(string feed, Channel channel)
    {
        string versionFileUrl = $"{feed}/Sdk/{channel.ToString()}/latest.version";
        Console.WriteLine("Fetching latest version from URL " + versionFileUrl);
        var body = await s_client.GetStringAsync(versionFileUrl);
        return body;
    }
}