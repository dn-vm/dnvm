using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde.Json;
using static Dnvm.Update.Result;

namespace Dnvm;

public sealed partial class Update
{
    private readonly Logger _logger;
    private readonly Command.UpdateOptions _options;
    private readonly string _feedUrl;

    public const string DefaultReleasesUrl = "https://commentout.com/dnvm/releases.json";

    public Update(Logger logger, Command.UpdateOptions options)
    {
        _logger = logger;
        _options = options;
        if (_options.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        var feed = _options.FeedUrl ?? DefaultConfig.FeedUrl;
        if (feed[^1] == '/')
        {
            feed = feed[..^1];
        }
        _feedUrl = feed;
    }

    public static Task<Result> Run(Logger logger, Command.UpdateOptions options)
    {
        return new Update(logger, options).Run();
    }

    public enum Result
    {
        Success,
        CouldntFetchIndex,
        NotASingleFile,
        SelfUpdateFailed
    }

    public async Task<Result> Run()
    {
        if (_options.Self)
        {
            return await UpdateSelf();
        }

        DotnetReleasesIndex versionIndex;
        try
        {
            versionIndex = await VersionInfoClient.FetchLatestIndex(_feedUrl);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Could not fetch the releases index: ");
            Console.Error.WriteLine(e.Message);
            return CouldntFetchIndex;
        }

        var manifest = ManifestUtils.ReadOrCreateManifest(ManifestUtils.ManifestPath);
        foreach (var tracked in manifest.TrackedChannels)
        {
            var latestOpt = versionIndex.GetLatestReleaseForChannel(tracked.ChannelName);
            if (latestOpt is {} latest)
            {
                _logger.Info($"Found version: {latest.LatestSdk}");
            }
            else
            {
                _logger.Warn($"No supported releases found for channel: {tracked.ChannelName}");
            }
        }
        return Success;
    }

    private async Task<Result> UpdateSelf()
    {
        if (!Utilities.IsSingleFile)
        {
            Console.WriteLine("Cannot self-update: the current executable is not deployed as a single file.");
            return Result.NotASingleFile;
        }

        string artifactDownloadLink = await GetReleaseLink();

        string tempArchiveDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        async Task HandleDownload(string tempDownloadPath)
        {
            _logger.Info("Extraction directory: " + tempArchiveDir);
            string? retMsg = await Utilities.ExtractArchiveToDir(tempDownloadPath, tempArchiveDir);
            if (retMsg != null)
            {
                _logger.Error("Extraction failed: " + retMsg);
            }
        }

        await DownloadBinaryToTempAndDelete(artifactDownloadLink, HandleDownload);
        _logger.Info($"{tempArchiveDir} contents: {string.Join(", ", Directory.GetFiles(tempArchiveDir))}");

        string dnvmTmpPath = Path.Combine(tempArchiveDir, Utilities.ExeName);
        bool success =
            await ValidateBinary(dnvmTmpPath) &&
            SwapWithRunningFile(dnvmTmpPath);
        return success ? Success : SelfUpdateFailed;
    }

    public async Task<string> GetReleaseLink()
    {
        var releasesUrl = _options.FeedUrl ?? DefaultReleasesUrl;
        string releasesJson = await Program.HttpClient.GetStringAsync(releasesUrl);
        _logger.Info("Releases JSON: " + releasesJson);
        var releases = JsonSerializer.Deserialize<Releases>(releasesJson);
        // Dnvm doesn't currently publish ARM64 binaries for any platform
        var rid = (Utilities.CurrentRID with {
            Arch = Architecture.X64
        }).ToString();
        var artifactDownloadLink = releases.LatestVersion.Artifacts[rid];
        _logger.Info("Artifact download link: " + artifactDownloadLink);
        return artifactDownloadLink;
    }

    private async Task DownloadBinaryToTempAndDelete(string uri, Func<string, Task> action)
    {
        string tempDownloadPath = Path.GetTempFileName();
        using (var tempFile = new FileStream(
            tempDownloadPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024 /* 64kB */,
            FileOptions.WriteThrough))
        {
            using var archiveHttpStream = await Program.HttpClient.GetStreamAsync(uri);
            await archiveHttpStream.CopyToAsync(tempFile);
            await tempFile.FlushAsync();
        }
        await action(tempDownloadPath);
    }

    public async Task<bool> ValidateBinary(string fileName)
    {
        // Replace with File.SetUnixFileMode when available
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = Process.Start("chmod", $"+x \"{fileName}\"");
            await chmod.WaitForExitAsync();
            _logger.Info("chmod return: " + chmod.ExitCode);
        }

        // Run exe and make sure it's OK
        var testProc = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            ArgumentList = { "--help" },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (testProc is Process ps)
        {
            await testProc.WaitForExitAsync();
            var output = await ps.StandardOutput.ReadToEndAsync();
            string error = await ps.StandardError.ReadToEndAsync();
            const string usageString = "usage: ";
            if (ps.ExitCode != 0)
            {
                _logger.Error("Could not run downloaded dnvm:");
                _logger.Error(error);
                return false;
            }
            else if (!output.Contains(usageString))
            {
                _logger.Error($"Downloaded dnvm did not contain \"{usageString}\": ");
                _logger.Log(output);
                return false;
            }
            return true;
        }
        return false;
    }

    public bool SwapWithRunningFile(string newFileName)
    {
        try
        {
            string backupPath = Utilities.ProcessPath + ".bak";
            _logger.Info($"Swapping {Utilities.ProcessPath} with downloaded version at {newFileName}");
            File.Move(Utilities.ProcessPath, backupPath, overwrite: true);
            File.Move(newFileName, Utilities.ProcessPath, overwrite: false);
            _logger.Log("Process successfully upgraded");
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                // Can't delete the open file on Windows
                File.Delete(backupPath);
            }
            return true;
        }
        catch (Exception e)
        {
            _logger.Error("Couldn't replace existing binary: " + e.Message);
            return false;
        }
    }
}