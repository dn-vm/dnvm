
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde.Json;
using static System.Environment;

namespace Dnvm;

public sealed class Install
{
    private static readonly string s_defaultInstallDir = Path.Combine(GetFolderPath(SpecialFolder.UserProfile), ".dotnet");
    private static readonly string s_globalInstallDir =
        Utilities.CurrentRID.OS == OSPlatform.Windows ? Path.Combine(GetFolderPath(SpecialFolder.ProgramFiles), "dotnet")
        : Utilities.CurrentRID.OS == OSPlatform.OSX ? "/usr/local/share/dotnet" // MacOS no longer lets anyone mess with /usr/share, even as root
        : "/usr/share/dotnet";

    private readonly string _installDir;
    private readonly string _manifestPath;

    private readonly Logger _logger;
    private readonly Command.InstallOptions _options;

    public enum Result
    {
        Success = 0,
        CouldntFetchLatestVersion,
        InstallLocationNotWritable,
        NotASingleFile,
        ExtractFailed,
        SelfInstallFailed,
    }

    public Install(Logger logger, Command.InstallOptions options)
    {
        _logger = logger;
        _options = options;
        if (_options.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        _installDir = options.InstallPath ??
            (options.Global ? s_globalInstallDir : s_defaultInstallDir);
        _manifestPath = Path.Combine(_installDir, "dnvmManifest.json");
    }

    public async Task<Result> Handle()
    {
        _logger.Info("Install Directory: " + _installDir);
        if (_options.Global)
        {
            try
            {
                Directory.CreateDirectory(_installDir);
                using var _ = File.OpenWrite(_manifestPath);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Error($"Cannot access global install location. Make sure you are running as {(Utilities.CurrentRID.OS == OSPlatform.Windows ? "administrator" : "root")}.");
                return Result.InstallLocationNotWritable;
            }
        }

        if (_options.Self)
        {
            return await RunSelfInstall() == 0
                ? Result.Success
                : Result.SelfInstallFailed;
        }

        var feeds = _options.FeedUrl is not null
            ? new[] { _options.FeedUrl }
            : new[] {
                "https://dotnetcli.azureedge.net/dotnet",
                "https://dotnetbuilds.azureedge.net/public"
            };

        string feed = feeds[0];

        RID rid = Utilities.CurrentRID;

        string? latestVersion = await GetLatestVersion(feed, _options.Channel, rid, Utilities.ZipSuffix);
        if (latestVersion is null)
        {
            Console.Error.WriteLine("Could not fetch the latest package version");
            return Result.CouldntFetchLatestVersion;
        }

        Manifest? manifest = null;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(_manifestPath));
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

        string archiveName = ConstructArchiveName(latestVersion, rid, Utilities.ZipSuffix);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        _logger.Info("Archive path: " + archivePath);

        string link = ConstructDownloadLink(feed, latestVersion, archiveName);
        _logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        _logger.Info("Existing manifest: " + result);

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough))
        using (var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
            await tempArchiveFile.FlushAsync();
        }
        _logger.Info($"Installing to {_installDir}");
        int extractResult = await ExtractArchiveToDir(archivePath, _installDir);
        File.Delete(archivePath);
        if (extractResult != 0)
        {
            return Result.ExtractFailed;
        }

        await AddToPath();

        var newWorkload = new Workload { Version = latestVersion };
        if (!manifest.Workloads.Contains(newWorkload))
        {
            _logger.Info($"Adding workload {newWorkload} to manifest.");
            manifest = manifest with { Workloads = manifest.Workloads.Add(newWorkload) };
        }
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest));
        _logger.Info("Writing manifest");
        return 0;
    }

    private async Task<int> LinuxAddToPath(string pathToAdd)
    {
        string addToPath = $"PATH=$PATH:{pathToAdd}";
        if (_options.Global)
        {
            _logger.Info($"Adding {pathToAdd} to the global PATH in /etc/profile.d/dotnet.sh");
            try
            {
                using (var f = File.OpenWrite("/etc/profile.d/dotnet.sh"))
                {
                    await f.WriteAsync(System.Text.Encoding.UTF8.GetBytes(addToPath).AsMemory());
                }
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Error("Unable to write to /etc/profile.d/dotnet.sh, attempting to write to local environment");
            }
        }
        return await UnixAddToPathInShellFiles(pathToAdd);
    }

    private async Task<int> MacAddToPath(string pathToAdd)
    {
        if (_options.Global)
        {
            _logger.Info($"Adding {pathToAdd} to the global PATH in /etc/paths.d/dotnet");
            try
            {
                using (var f = File.OpenWrite("/etc/paths.d/dotnet"))
                {
                    await f.WriteAsync(System.Text.Encoding.UTF8.GetBytes(pathToAdd).AsMemory());
                }
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Error("Unable to write path to /etc/paths.d/dotnet, attempting to write to local environment");
            }
        }
        return await UnixAddToPathInShellFiles(pathToAdd);
    }

    static async Task<int> ExtractArchiveToDir(string archivePath, string dirPath)
    {
        Directory.CreateDirectory(dirPath);
        if (Utilities.CurrentRID.OS != OSPlatform.Windows)
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
        else
        {
            try
            {
                ZipFile.ExtractToDirectory(archivePath, dirPath);
            }
            catch
            {
                return 1;
            }
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
        return $"{feed}Sdk/{latestVersion}/{archiveName}";
    }

    private async Task<string?> GetLatestVersion(
        string feed,
        Channel channel,
        RID rid,
        string suffix)
    {
        string latestVersion;
        // The dotnet service provides an endpoint for fetching the latest LTS and Current versions
        if (channel != Channel.Preview)
        {
            string versionFileUrl = $"{feed}Sdk/{channel.ToString()}/latest.version";
            _logger.Info("Fetching latest version from URL " + versionFileUrl);
            latestVersion = await Program.DefaultClient.GetStringAsync(versionFileUrl);
        }
        else
        {
            // There's no endpoint for preview versions. We'll have to construct that ourselves from aka.ms.
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

        var procPath = Utilities.ProcessPath;
        _logger.Info("Location of running exe" + procPath);

        var targetPath = Path.Combine(_installDir, Utilities.ExeName);
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
        await AddToPath();

        return 0;
    }

    private async Task<int> AddToPath()
    {
        if (Utilities.CurrentRID.OS == OSPlatform.Windows)
        {
            Console.WriteLine("Adding install directory to user path: " + _installDir);
            WindowsAddToPath(_installDir);
        }
        else if (Utilities.CurrentRID.OS == OSPlatform.OSX)
        {
            int result = await MacAddToPath(_installDir);
            if (result != 0)
            {
                _logger.Error("Failed to add to path");
            }
        }
        else
        {
            int result = await LinuxAddToPath(_installDir);
            if (result != 0)
            {
                _logger.Error("Failed to add to path");
            }
        }
        return 0;
    }

    private int WindowsAddToPath(string pathToAdd)
    {
        var currentPathVar = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        if (!(":" + currentPathVar + ":").Contains(_installDir))
        {
            Environment.SetEnvironmentVariable("PATH", _installDir + ":" + currentPathVar, _options.Global ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User);
        }
        return 0;
    }

    private async Task<int> UnixAddToPathInShellFiles(string pathToAdd)
    {
        _logger.Info("Setting environment variables in shell files");
        string resolvedEnvPath = Path.Combine(pathToAdd, "env");
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
                await writer.WriteAsync(s_envShContent.Replace("{install_loc}", pathToAdd));
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
