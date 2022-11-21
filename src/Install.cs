
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
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
    private static readonly string s_defaultInstallDir = Path.Combine(GetFolderPath(SpecialFolder.LocalApplicationData, SpecialFolderOption.DoNotVerify), "dnvm");
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
        ManifestIOError,
        ManifestFileCorrupted,
        ChannelAlreadyTracked
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
        _manifestPath = Path.Combine(_installDir, ManifestUtils.FileName);
    }

    public async Task<Result> Handle()
    {
        _logger.Info("Install Directory: " + _installDir);
        try
        {
            Directory.CreateDirectory(_installDir);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error($"Cannot write to install location. Ensure you have appropriate permissions.");
            return Result.InstallLocationNotWritable;
        }

        if (_options.Self)
        {
            return await RunSelfInstall() == 0
                ? Result.Success
                : Result.SelfInstallFailed;
        }

        string? text = null;
        try
        {
            text = File.ReadAllText(_manifestPath);
        }
        // Not found is expected
        catch (DirectoryNotFoundException) {}
        catch (FileNotFoundException) {}
        catch (Exception e)
        {
            Console.Error.WriteLine("Error reading manifest file: " + e.Message);
            return Result.ManifestIOError;
        }

        Manifest manifest;
        if (text is not null)
        {
            var manifestOpt = ManifestUtils.ReadNewOrOldManifest(text);
            if (manifestOpt is null)
            {
                Console.Error.WriteLine("Manifest file corrupted");
                return Result.ManifestFileCorrupted;
            }
            else
            {
                manifest = manifestOpt;
            }
        }
        else
        {
            manifest = new Manifest() {
                InstalledVersions = ImmutableArray<string>.Empty,
                TrackedChannels = ImmutableArray<TrackedChannel>.Empty
            };
        }

        if (manifest.TrackedChannels.Any(c => c.ChannelName == _options.Channel))
        {
            Console.WriteLine($"Channel '{_options.Channel}' is already being tracked." +
                " Did you mean to run 'dnvm update'?");
            return Result.ChannelAlreadyTracked;
        }

        var feeds = _options.FeedUrl is not null
            ? new[] { _options.FeedUrl }
            : new[] {
                "https://dotnetcli.azureedge.net/dotnet",
                "https://dotnetbuilds.azureedge.net/public"
            };

        var feed = feeds[0];
        if (feed[^1] == '/')
        {
            feed = feed[..^1];
        }

        RID rid = Utilities.CurrentRID;

        string? latestVersion = await GetLatestVersion(feed, _options.Channel, rid, Utilities.ZipSuffix);
        if (latestVersion is null)
        {
            Console.Error.WriteLine("Could not fetch the latest package version");
            return Result.CouldntFetchLatestVersion;
        }
        _logger.Log("Found latest version: " + latestVersion);

        if (!_options.Force && manifest.InstalledVersions.Contains(latestVersion))
        {
            _logger.Log($"Version {latestVersion} is already installed." +
                " Skipping installation. To install anyway, pass --force.");
            return 0;
        }

        string archiveName = ConstructArchiveName(latestVersion, rid, Utilities.ZipSuffix);
        string archivePath = Path.Combine(Path.GetTempPath(), archiveName);
        _logger.Info("Archive path: " + archivePath);

        var link = ConstructDownloadLink(feed, latestVersion, archiveName);
        _logger.Info("Download link: " + link);

        var result = JsonSerializer.Serialize(manifest);
        _logger.Info("Existing manifest: " + result);

        using (var tempArchiveFile = File.Create(archivePath, 64 * 1024 /* 64kB */, FileOptions.WriteThrough))
        using (var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(link))
        {
            await archiveHttpStream.CopyToAsync(tempArchiveFile);
            await tempArchiveFile.FlushAsync();
        }
        _logger.Log($"Installing to {_installDir}");
        string? extractResult = await Utilities.ExtractArchiveToDir(archivePath, _installDir);
        File.Delete(archivePath);
        if (extractResult != null)
        {
            _logger.Error("Extract failed: " + extractResult);
            return Result.ExtractFailed;
        }

        if (_options.UpdateUserEnvironment)
        {
            await AddToPath();
        }

        _logger.Info($"Adding installed version '{latestVersion}' to manifest.");
        manifest = manifest with { InstalledVersions = manifest.InstalledVersions.Add(latestVersion) };
        var tracked = new TrackedChannel {
            ChannelName = _options.Channel,
            InstalledVersions = ImmutableArray.Create(latestVersion)
        };
        _logger.Info($"Adding channel {tracked} to manifest.");
        manifest = manifest with { TrackedChannels = manifest.TrackedChannels.Add(tracked) };

        _logger.Info("Writing manifest");
        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, JsonSerializer.Serialize(manifest));
        File.Move(tmpFile, _manifestPath, overwrite: true);

        _logger.Log("Successfully installed");
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
        // The dotnet service provides an endpoint for fetching the latest LTS and Current versions
        if (channel != Channel.Preview)
        {
            var versionFileUrl = $"{feed}/Sdk/{channel}/latest.version";
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

    private static string GetEnvShContent()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("dnvm.env.sh")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task<int> RunSelfInstall()
    {
        if (!Utilities.IsSingleFile)
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
        var portableEnvPath = resolvedEnvPath.Replace(Environment.GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify), "$HOME");
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
                await writer.WriteAsync(GetEnvShContent().Replace("{install_loc}", pathToAdd));
                await envFile.FlushAsync();
            }

            // Scan shell files for shell suffix and add it if it doesn't exist
            _logger.Log("Scanning for shell files to update");
            foreach (var shellFileName in ProfileShellFiles)
            {
                var shellPath = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile, SpecialFolderOption.DoNotVerify), shellFileName);
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
