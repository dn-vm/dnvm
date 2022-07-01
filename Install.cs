
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using dnvm;
using Serde.Json;
using static System.Environment;

namespace Dnvm;

sealed class Install
{
    private static readonly string s_installDir = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".dotnet");
    private static readonly string s_manifestPath = Path.Combine(s_installDir, "dnvmManifest.json");

    private readonly Logger _logger;
    private readonly Command.InstallOptions _options;

    public Install(Logger logger, Command.InstallOptions options)
    {
        _logger = logger;
        _options = options;
        if (_options.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
    }

    public async Task<int> Handle()
    {
        if (_options.Self)
        {
            return await RunSelfInstall();
        }

        _logger.Info("Install Directory: " + s_installDir);

        var feeds = new[] {
            "https://dotnetcli.azureedge.net/dotnet",
            "https://dotnetbuilds.azureedge.net/public"
        };

        string feed = feeds[0];

        RID rid = Program.Rid;

        string suffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "zip"
            : "tar.gz";

        string? latestVersion = await GetLatestVersion(feed, _options.Channel, rid, suffix);
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

        if (!_options.Force && manifest.Workloads.Contains(new Workload() { Version = latestVersion }))
        {
            Console.WriteLine($"Version {latestVersion} is the latest available version and is already installed.");
            return 0;
        }

        string archiveName = ConstructArchiveName(latestVersion, rid, suffix);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        _logger.Info("Archive path: " + archivePath);

        string link = ConstructDownloadLink(feed, latestVersion, archiveName);
        _logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        _logger.Info("Existing manifest: " + link);

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough | FileOptions.DeleteOnClose))
        using (var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(link))
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
        RID rid,
        string suffix)
    {
        return specificVersion is null
            ? $"dotnet-sdk-{rid}.{suffix}"
            : $"dotnet-sdk-{specificVersion}-{rid}.{suffix}";
    }

    private static readonly HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });

    static string ConstructDownloadLink(string feed, string latestVersion, string archiveName)
    {
        return $"{feed}/Sdk/{latestVersion}/{archiveName}";
    }

    private async Task<string?> GetLatestVersion(
        string feed,
        Channel channel,
        RID rid,
        string suffix)
    {
        string latestVersion;
        // The dotnet service provides an endpoint for fetching the latest LTS and Current versions,
        // but not preview. We'll have to construct that ourselves from aka.ms.
        if (channel != Channel.Preview)
        {
            string versionFileUrl = $"{feed}/Sdk/{channel.ToString()}/latest.version";
            _logger.Info("Fetching latest version from URL " + versionFileUrl);
            latestVersion = await Program.DefaultClient.GetStringAsync(versionFileUrl);
        }
        else
        {
            const string PreviewMajorVersion = "7.0";
            var versionlessArchiveName = ConstructArchiveName(null, rid, suffix);
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

    private const string s_envShContent = """
#!/bin/sh
# Prepend dotnet dir to the path, unless it's already there.
# Steal rustup trick of matching with ':' on both sides
case ":${PATH}:" in
    *:{install_loc}:*)
        ;;
    *)
        export PATH="{install_loc}:$PATH"
        ;;
esac
""";

    private async Task<int> RunSelfInstall()
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
        if (!_options.Force && File.Exists(targetPath))
        {
            _logger.Log("dnvm is already installed at: " + targetPath);
            _logger.Log("Did you mean to run `dnvm update`? Otherwise, the '--force' flag is required to overwrite the existing file.");
        }
        else
        {
            try
            {
                _logger.Info($"Copying file from '{procPath}' to '{targetPath}'");
                File.Copy(procPath, targetPath, overwrite: _options.Force);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not copy file from '{procPath}' to '{targetPath}': {e.Message}");
                return 1;
            }

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
        }
        else
        {
            _logger.Info("Setting environment variables");
            string resolvedEnvPath = Path.Combine(s_installDir, "env");
            // Using the full path to the install directory is usually fine, but on Unix systems
            // people often copy their dotfiles from one machine to another and fully resolved paths present a problem
            // there. Instead, we'll try to replace instances of the user's home directory with the $HOME
            // variable, which should be the most common case of machine-dependence.
            var portableEnvPath = resolvedEnvPath.Replace(Environment.GetFolderPath(SpecialFolder.UserProfile), "$HOME");
            string userShSuffix = $"""
if [ -f "{portableEnvPath}" ]; then
    . "{portableEnvPath}"
fi
""";
            FileStream? envFile;
            try
            {
                envFile = File.Open(resolvedEnvPath, FileMode.CreateNew);
            }
            catch
            {
                _logger.Info("env file already exists, skipping installation");
                envFile = null;
            }

            if (envFile is not null)
            {
                _logger.Info("Writing env sh file");
                using (envFile)
                using (var writer = new StreamWriter(envFile))
                {
                    await writer.WriteAsync(s_envShContent.Replace("{install_loc}", s_installDir));
                    await envFile.FlushAsync();
                }

                // Scan shell files for shell suffix and add it if it doesn't exist
                _logger.Log("Scanning for shell files to update");
                foreach (var shellFileName in ProfileShellFiles)
                {
                    var shellPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), shellFileName);
                    _logger.Info("Checking for file: " + shellPath);
                    if (File.Exists(shellPath))
                    {
                        _logger.Log("Found " + shellPath);
                    }
                    else
                    {
                        continue;
                    }
                    try
                    {
                        if (!(await FileContainsLine(shellPath, $". \"{portableEnvPath}\"")))
                        {
                            _logger.Log("Adding env import to: " + shellPath);
                            await File.AppendAllTextAsync(shellPath, userShSuffix);
                        }
                    }
                    catch (Exception e)
                    {
                        // Ignore if the file can't be accessed
                        _logger.Info($"Couldn't write to file {shellPath}: {e.Message}");
                    }
                }
            }
        }

        return 0;
    }

    private static ImmutableArray<string> ProfileShellFiles => ImmutableArray.Create<string>(
        ".profile",
        ".bashrc",
        ".zshrc"
    );

    private static async Task<bool> FileContainsLine(string filePath, string contents)
    {
        using var file = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open);
        var stream = file.CreateViewStream();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.Contains(contents))
            {
                return true;
            }
        }
        return false;
    }
}