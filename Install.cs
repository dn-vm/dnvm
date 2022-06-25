
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde.Json;
using static System.Environment;

namespace Dnvm;

sealed class Install
{
    private static readonly HttpClient s_client = new HttpClient();
    private static readonly string s_installDir = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".dotnet");
    private static readonly string s_manifestPath = Path.Combine(s_installDir, "dnvmManifest.json");

    private readonly Logger _logger;

    public Install(Logger logger)
    {
        _logger = logger;
    }

    public async Task<int> Handle(Command.InstallOptions options)
    {
        if (options.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }

        if (options.Self)
        {
            return RunSelfInstall();
        }

        _logger.Info("Install Directory: " + s_installDir);

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

        if (!options.Force && manifest.Workloads.Contains(new Workload() { Version = latestVersion }))
        {
            Console.WriteLine($"Version {latestVersion} is the latest available version and is already installed.");
            return 0;
        }

        string archiveName = ConstructArchiveName(latestVersion, osName, arch, suffix);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        _logger.Info("Archive path: " + archivePath);

        string link = ConstructDownloadLink(feed, latestVersion, archiveName);
        _logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        _logger.Info("Existing manifest: " + link);

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough | FileOptions.DeleteOnClose))
        using (var archiveHttpStream = await s_client.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
            await tempArchiveFile.FlushAsync();
            if (await ExtractArchiveToDir(archivePath, s_installDir) != 0)
            {
                return 1;
            }
        }

        var newWorkload = new Workload { Version = latestVersion };
        if (!manifest.Workloads.Contains(newWorkload))
        {
            _logger.Info($"Adding workload {newWorkload} to manifest.");
            manifest = manifest with { Workloads = manifest.Workloads.Add(newWorkload) };
        }
        File.WriteAllText(s_manifestPath, JsonSerializer.Serialize(manifest));
        _logger.Info("Writing manifest");
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

    private async Task<string?> GetLatestVersion(
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
            _logger.Info("Fetching latest version from URL " + versionFileUrl);
            latestVersion = await s_client.GetStringAsync(versionFileUrl);
        }
        else
        {
            const string PreviewMajorVersion = "7.0";
            var versionlessArchiveName = ConstructArchiveName(null, osName, arch, suffix);
            string akaMsUrl = $"https://aka.ms/dotnet/{PreviewMajorVersion}/preview/{versionlessArchiveName}";
            _logger.Info("aka.ms URL: " + akaMsUrl);
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
        _logger.Info(latestVersion);
        return latestVersion;
    }

    private int RunSelfInstall()
    {
        if (Assembly.GetEntryAssembly()?.Location != "")
        {
            Console.WriteLine("Cannot self-install into target location: the current executable is not deployed as a single file.");
            return 1;
        }
        var procPath = Process.GetCurrentProcess().MainModule!.FileName;
        var exeName = "dnvm" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ".exe"
            : "");
        _logger.Info("Location of running exe" + procPath);

        var targetPath = Path.Combine(s_installDir, exeName);
        try
        {
            File.Copy(procPath, targetPath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
            return 1;
        }

        // Set up path
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("Adding install directory to user path: " + s_installDir);
            var currentPathVar = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            if (!(":" + currentPathVar + ":").Contains(s_installDir))
            {
                Environment.SetEnvironmentVariable("PATH", s_installDir + ":" + currentPathVar, EnvironmentVariableTarget.User);
            }
            else
            {
                string envPath = Path.Combine(s_installDir, "env");
                const string userShSuffix = $$"""
if [ -f "{envPath}" ]; then
    . "{s_installDir}"
""";
            }
        }

        return 0;
    }
}