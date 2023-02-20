using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Semver;
using Serde.Json;
using static Dnvm.Update.Result;

namespace Dnvm;

public sealed partial class Update
{
    private readonly Logger _logger;
    private readonly CommandArguments.UpdateArguments _args;
    private readonly string _feedUrl;
    private readonly string _releasesUrl;
    private readonly string _manifestPath;
    private readonly string _sdkInstallDir;

    public const string DefaultReleasesUrl = "https://github.com/dn-vm/dn-vm.github.io/raw/gh-pages/releases.json";

    public Update(GlobalOptions options, Logger logger, CommandArguments.UpdateArguments args)
    {
        _logger = logger;
        _args = args;
        if (_args.Verbose)
        {
            _logger.LogLevel = LogLevel.Info;
        }
        _feedUrl = _args.FeedUrl ?? GlobalOptions.DotnetFeedUrl;
        if (_feedUrl[^1] == '/')
        {
            _feedUrl = _feedUrl[..^1];
        }
        _releasesUrl = _args.DnvmReleasesUrl ?? DefaultReleasesUrl;
        _manifestPath = options.ManifestPath;
        _sdkInstallDir = options.SdkInstallDir;
    }

    public static Task<Result> Run(GlobalOptions options, Logger logger, CommandArguments.UpdateArguments args)
    {
        return new Update(options, logger, args).Run();
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
        if (_args.Self)
        {
            return await UpdateSelf();
        }

        DotnetReleasesIndex releaseIndex;
        try
        {
            releaseIndex = await DotnetReleasesIndex.FetchLatestIndex(_feedUrl);
        }
        catch (Exception e)
        {
            _logger.Error("Could not fetch the releases index: ");
            _logger.Error(e.Message);
            return CouldntFetchIndex;
        }

        var manifest = ManifestUtils.ReadOrCreateManifest(_manifestPath);
        return await UpdateSdks(
            _logger,
            releaseIndex,
            manifest,
            _args.Yes,
            _feedUrl,
            _releasesUrl,
            _manifestPath,
            _sdkInstallDir);
    }

    public static async Task<Result> UpdateSdks(
        Logger logger,
        DotnetReleasesIndex releasesIndex,
        Manifest manifest,
        bool yes,
        string feedUrl,
        string releasesUrl,
        string manifestPath,
        string sdkInstallDir)
    {
        logger.Log("Looking for available updates");
        // Check for dnvm updates
        if (await CheckForSelfUpdates(logger, releasesUrl) is (true, _))
        {
            logger.Log("dnvm is out of date. Run 'dnvm update --self' to update dnvm.");
        }
        var updateResults = FindPotentialUpdates(manifest, releasesIndex);
        if (updateResults.Count > 0)
        {
            logger.Log("Found versions available for update");
            logger.Log("Channel\tInstalled\tAvailable");
            logger.Log("-------------------------------------------------");
            foreach (var (c, newestInstalled, newestAvailable) in updateResults)
            {
                logger.Log($"{c}\t{newestInstalled}\t{newestAvailable.LatestSdk}");
            }
            logger.Log("Install updates? [y/N]: ");
            var response = yes ? "y" : Console.ReadLine();
            if (response?.Trim().ToLowerInvariant() == "y")
            {
                foreach (var (c, _, newestAvailable) in updateResults)
                {
                    _ = await Install.InstallSdk(
                        logger,
                        c,
                        newestAvailable.LatestSdk,
                        Utilities.CurrentRID,
                        feedUrl,
                        manifest,
                        manifestPath,
                        sdkInstallDir
                        );
                }
            }
        }
        return Success;
    }

    public static List<(Channel TrackedChannel, SemVersion NewestInstalled, DotnetReleasesIndex.Release NewestAvailable)> FindPotentialUpdates(
        Manifest manifest,
        DotnetReleasesIndex releaseIndex)
    {
        var list = new List<(Channel, SemVersion, DotnetReleasesIndex.Release)>();
        foreach (var tracked in manifest.TrackedChannels)
        {
            var newestInstalled = tracked.InstalledSdkVersions
                .Select(v => SemVersion.Parse(v, SemVersionStyles.Strict))
                .Max(SemVersion.PrecedenceComparer)!;
            var release = releaseIndex.GetLatestReleaseForChannel(tracked.ChannelName);
            if (release is { LatestSdk: var sdkVersion} &&
                SemVersion.TryParse(sdkVersion, SemVersionStyles.Strict, out var newestAvailable) &&
                SemVersion.ComparePrecedence(newestInstalled, newestAvailable) < 0)
            {
                list.Add((tracked.ChannelName, newestInstalled!, release));
            }
        }
        return list;
    }

    private async Task<Result> UpdateSelf()
    {
        if (!Utilities.IsSingleFile)
        {
            _logger.Error("Cannot self-update: the current executable is not deployed as a single file.");
            return Result.NotASingleFile;
        }

        if (await CheckForSelfUpdates(_logger, _releasesUrl) is not (true, DnvmReleases releases))
        {
            return Result.Success;
        }

        string artifactDownloadLink = GetReleaseLink(releases);

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

        string dnvmTmpPath = Path.Combine(tempArchiveDir, Utilities.DnvmExeName);
        bool success = await ValidateBinary(_logger, dnvmTmpPath);
        if (!success)
        {
            return SelfUpdateFailed;
        }
        RunSelfInstall(dnvmTmpPath);
        return Success;
    }

    private static async Task<(bool UpdateAvailable, DnvmReleases? Releases)> CheckForSelfUpdates(
        Logger logger,
        string releasesUrl)
    {
        logger.Log("Checking for updates to dnvm");
        logger.Info("Using dnvm releases URL: " + releasesUrl);

        string releasesJson;
        try
        {
            releasesJson = await Program.HttpClient.GetStringAsync((string)releasesUrl);
        }
        catch (Exception e)
        {
            logger.Error((string)("Could not fetch releases from URL: " + releasesUrl));
            logger.Error(e.Message);
            return (false, null);
        }

        DnvmReleases releases;
        try
        {
            logger.Info("Releases JSON: " + releasesJson);
            releases = JsonSerializer.Deserialize<DnvmReleases>(releasesJson);
        }
        catch (Exception e)
        {
            logger.Error("Could not deserialize Releases JSON: " + e.Message);
            return (false, null);
        }

        var current = Program.SemVer;
        var newest = SemVersion.Parse(releases.LatestVersion.Version, SemVersionStyles.Strict);

        if (current.ComparePrecedenceTo(newest) < 0)
        {
            logger.Log("Found newer version: " + newest);
            return (true, releases);
        }
        else
        {
            logger.Log("No newer version found. Dnvm is up-to-date.");
            return (false, releases);
        }
    }

    private string GetReleaseLink(DnvmReleases releases)
    {
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

    public static async Task<bool> ValidateBinary(Logger logger, string fileName)
    {
        // Replace with File.SetUnixFileMode when available
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = Process.Start("chmod", $"+x \"{fileName}\"");
            await chmod.WaitForExitAsync();
            logger.Info("chmod return: " + chmod.ExitCode);
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
                logger.Error("Could not run downloaded dnvm:");
                logger.Error(error);
                return false;
            }
            else if (!output.Contains(usageString))
            {
                logger.Error($"Downloaded dnvm did not contain \"{usageString}\": ");
                logger.Log(output);
                return false;
            }
            return true;
        }
        return false;
    }

    public void RunSelfInstall(string newFileName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = newFileName,
            ArgumentList = { "install", "--self", "--update" }
        };
        _ = Process.Start(psi);
    }
}