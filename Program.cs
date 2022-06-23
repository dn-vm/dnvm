
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde.Json;
using static System.Environment;

namespace Dnvm;

public static class Program
{
    private static readonly HttpClient s_client = new HttpClient();
    private static readonly string s_installDir = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".dotnet");
    private static readonly string s_manifestPath = Path.Combine(s_installDir, "dnvmManifest.json");

    private static Logger? Logger = null;

    static async Task<int> Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        var feeds = new[] {
            "https://dotnetcli.azureedge.net/dotnet",
            "https://dotnetbuilds.azureedge.net/public"
        };

        if (options.Verbose) {
            Logger = new Logger();
        }

        Logger?.Log("Install Directory: " + s_installDir);

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

        string? latestVersion = await GetLatestVersion(feed, options.Channel, osName, arch, suffix);
        if (latestVersion is null)
        {
            Console.Error.WriteLine("Could not fetch the latest package version");
            return 1;
        }

        Manifest? manifest = null;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(s_manifestPath));
        }
        catch
        {
            // Ignore exceptions
            manifest = new Manifest();
        }

        if (!options.Force)
        {
            foreach (var workload in manifest.Workloads)
            {
                if (workload.Version == latestVersion)
                {
                    Console.WriteLine($"Version {latestVersion} is the latest available version and is already installed.");
                    return 0;
                }
            }
        }

        string archiveName = ConstructArchiveName(latestVersion, osName, arch, suffix);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        Logger?.Log("Archive path: " + archivePath);

        string link = ConstructDownloadLink(feed, latestVersion, archiveName);
        Logger?.Log("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        Logger?.Log("Existing manifest: " + link);

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.DeleteOnClose))
        using (var archiveHttpStream = await s_client.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
            if (await ExtractArchiveToDir(archivePath, s_installDir) != 0)
            {
                return 1;
            }
        }

        var newWorkload = new Workload { Version = latestVersion };
        if (manifest.Workloads.Contains(newWorkload))
        {
            manifest = manifest with { Workloads = manifest.Workloads.Add(newWorkload) };
        }
        File.WriteAllText(s_manifestPath, JsonSerializer.Serialize(manifest));
        return 0;
    }

    static async Task<int> ExtractArchiveToDir(string archivePath, string dirPath)
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
                return p.ExitCode;
            }
            return 1;
        }
        return 0;
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

    private static readonly HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });

    static string ConstructDownloadLink(string feed, string latestVersion, string archiveName)
    {
        return $"{feed}/Sdk/{latestVersion}/{archiveName}";
    }

    private static async Task<string?> GetLatestVersion(
        string feed,
        Channel channel,
        string osName,
        string arch,
        string suffix)
    {
        string latestVersion;
        // The dotnet service provides an endpoint for fetching the latest LTS and Current versions,
        // but not preview. We'll have to construct that ourselves from aka.ms.
        if (channel != Channel.Preview)
        {
            string versionFileUrl = $"{feed}/Sdk/{channel.ToString()}/latest.version";
            Logger?.Log("Fetching latest version from URL " + versionFileUrl);
            latestVersion = await s_client.GetStringAsync(versionFileUrl);
        }
        else
        {
            const string PreviewMajorVersion = "7.0";
            var versionlessArchiveName = ConstructArchiveName(null, osName, arch, suffix);
            string akaMsUrl = $"https://aka.ms/dotnet/{PreviewMajorVersion}/preview/{versionlessArchiveName}";
            Logger?.Log("aka.ms URL: " + akaMsUrl);
            var requestMessage = new HttpRequestMessage(
                HttpMethod.Head,
                akaMsUrl);
            var response = await s_noRedirectClient.SendAsync(requestMessage);

            if (response.StatusCode != HttpStatusCode.MovedPermanently)
            {
                return null;
            }

            if (response.Headers.Location?.Segments is not { Length: 5 } segments)
            {
                return null;
            }

            latestVersion = segments[3].TrimEnd('/');
        }
        Logger?.Log(latestVersion);
        return latestVersion;
    }
}