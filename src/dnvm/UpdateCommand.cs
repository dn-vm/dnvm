using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dnvm.Signing;
using Semver;
using Serde.Json;
using Spectre.Console;
using StaticCs;
using static Dnvm.UpdateCommand.Result;

namespace Dnvm;

public sealed partial class UpdateCommand
{
    public sealed record Options
    {
        /// <summary>
        /// URL to the dnvm releases.json file listing the latest releases and their download
        /// locations.
        /// </summary>
        public string? DnvmReleasesUrl { get; init; }
        public string? FeedUrl { get; init; }
        public bool Verbose { get; init; } = false;
        public bool Self { get; init; } = false;
        /// <summary>
        /// Implicitly answers 'yes' to every question.
        /// </summary>
        public bool Yes { get; init; } = false;
    }

    private readonly DnvmEnv _env;
    private readonly Logger _logger;
    private readonly IEnumerable<string> _feedUrls;
    private readonly string _releasesUrl;
    private readonly bool _yes;
    private readonly bool _self;

    public const string ReleaseKeyFileName = "relkeys.pub";
    public const string ReleaseKeySigFileName = ReleaseKeyFileName + ".sig";

    public UpdateCommand(DnvmEnv env, Logger logger, Options opts)
    {
        _logger = logger;
        if (opts.Verbose)
        {
            _logger.Enabled = true;
        }
        _feedUrls = opts.FeedUrl is not null
            ? [opts.FeedUrl.TrimEnd('/')]
            : env.DotnetFeedUrls;
        _releasesUrl = opts.DnvmReleasesUrl ?? env.DnvmReleasesUrl;
        _env = env;
        _yes = opts.Yes;
        _self = opts.Self;
    }

    public static Task<Result> Run(DnvmEnv env, Logger logger, DnvmSubCommand.UpdateArgs args)
    {
        return Run(env, logger, new Options
        {
            DnvmReleasesUrl = args.DnvmReleasesUrl,
            FeedUrl = args.FeedUrl,
            Verbose = args.Verbose ?? false,
            Self = args.Self ?? false,
            Yes = args.Yes ?? false
        });
    }

    public static Task<Result> Run(DnvmEnv env, Logger logger, Options options)
    {
        return new UpdateCommand(env, logger, options).Run();
    }

    public enum Result
    {
        Success,
        CouldntFetchIndex,
        NotASingleFile,
        SelfUpdateFailed,
        UpdateFailed
    }

    public async Task<Result> Run()
    {
        var manifest = await DnvmEnv.ReadOrCreateManifest(_env);
        if (_self)
        {
            return await UpdateSelf(manifest);
        }

        DotnetReleasesIndex releaseIndex;
        try
        {
            releaseIndex = await DotnetReleasesIndex.FetchLatestIndex(_env.HttpClient, _feedUrls);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _env.Console.Error($"Could not fetch the releases index: {e.Message}");
            return CouldntFetchIndex;
        }

        return await UpdateSdks(
            _env,
            _logger,
            releaseIndex,
            manifest,
            _yes,
            _releasesUrl);
    }

    public static async Task<Result> UpdateSdks(
        DnvmEnv env,
        Logger logger,
        DotnetReleasesIndex releasesIndex,
        Manifest manifest,
        bool yes,
        string releasesUrl)
    {
        env.Console.WriteLine("Looking for available updates");
        // Check for dnvm updates
        if (await CheckForSelfUpdates(env.HttpClient, env.Console, logger, releasesUrl, manifest.PreviewsEnabled) is (true, _))
        {
            env.Console.WriteLine("dnvm is out of date. Run 'dnvm update --self' to update dnvm.");
        }

        try
        {
            var updateResults = FindPotentialUpdates(manifest, releasesIndex);
            if (updateResults.Count > 0)
            {
                env.Console.WriteLine("Found versions available for update");
                var table = new Table();
                table.AddColumn("Channel");
                table.AddColumn("Installed");
                table.AddColumn("Available");
                foreach (var (c, newestInstalled, newestAvailable, _) in updateResults)
                {
                    table.AddRow(c.ToString(), newestInstalled?.ToString() ?? "(none)", newestAvailable.LatestSdk);
                }
                env.Console.Write(table);
                env.Console.WriteLine("Install updates? [y/N]: ");
                var response = yes ? "y" : Console.ReadLine();
                if (response?.Trim().ToLowerInvariant() == "y")
                {
                    // Find releases
                    var releasesToInstall = new HashSet<(ChannelReleaseIndex.Component, ChannelReleaseIndex.Release, SdkDirName)>();
                    foreach (var (c, _, newestAvailable, sdkDir) in updateResults)
                    {
                        var latestSdkVersion = SemVersion.Parse(newestAvailable.LatestSdk, SemVersionStyles.Strict);

                        var result = await InstallCommand.TryGetReleaseFromIndex(env.HttpClient, releasesIndex, c, latestSdkVersion);
                        if (result is not ({} component, {} release))
                        {
                            env.Console.Error($"Index does not contain release for channel '{c}' with version '{latestSdkVersion}'."
                                + "This is either a bug or the .NET index is incorrect. Please a file a bug at https://github.com/dn-vm/dnvm.");
                            return UpdateFailed;
                        }
                        releasesToInstall.Add((component, release, sdkDir));
                    }

                    // Install releases
                    foreach (var (component, release, sdkDir) in releasesToInstall)
                    {
                        var latestSdkVersion = release.Sdk.Version;
                        var result = await InstallCommand.InstallSdk(
                            env,
                            manifest,
                            component,
                            release,
                            sdkDir,
                            logger);

                        if (result is not Result<Manifest, InstallCommand.InstallError>.Ok(var newManifest))
                        {
                            env.Console.Error($"Failed to install version '{latestSdkVersion}'");
                            return UpdateFailed;
                        }
                        manifest = newManifest;
                    }

                    // Update manifest for tracked channels
                    foreach (var (c, _, newestAvailable, _) in updateResults)
                    {
                        var latestSdkVersion = SemVersion.Parse(newestAvailable.LatestSdk, SemVersionStyles.Strict);


                        foreach (var oldTracked in manifest.RegisteredChannels.Where(t => t.ChannelName == c))
                        {
                            var newTracked = oldTracked with
                            {
                                InstalledSdkVersions = oldTracked.InstalledSdkVersions.Add(latestSdkVersion)
                            };
                            manifest = manifest with { RegisteredChannels = manifest.RegisteredChannels.Replace(oldTracked, newTracked) };
                        }
                    }
                }
            }
        }
        finally
        {
            logger.Log("Writing manifest");
            await env.WriteManifest(manifest);
        }

        env.Console.WriteLine("Successfully installed");
        return Success;
    }

    public static List<(Channel TrackedChannel, SemVersion? NewestInstalled, DotnetReleasesIndex.ChannelIndex NewestAvailable, SdkDirName SdkDir)> FindPotentialUpdates(
        Manifest manifest,
        DotnetReleasesIndex releaseIndex)
    {
        var list = new List<(Channel, SemVersion?, DotnetReleasesIndex.ChannelIndex, SdkDirName)>();
        foreach (var tracked in manifest.RegisteredChannels)
        {
            var newestInstalled = tracked.InstalledSdkVersions
                .Max(SemVersion.PrecedenceComparer);
            var release = releaseIndex.GetChannelIndex(tracked.ChannelName);
            if (release is { LatestSdk: var sdkVersion} &&
                SemVersion.TryParse(sdkVersion, SemVersionStyles.Strict, out var newestAvailable) &&
                SemVersion.ComparePrecedence(newestInstalled, newestAvailable) < 0)
            {
                list.Add((tracked.ChannelName, newestInstalled, release, tracked.SdkDirName));
            }
        }
        return list;
    }

    public async Task<Result> UpdateSelf(Manifest manifest)
    {
        if (!Utilities.IsSingleFile)
        {
            _env.Console.Error("Cannot self-update: the current executable is not deployed as a single file.");
            return Result.NotASingleFile;
        }

        DnvmReleases.Release release;
        switch (await CheckForSelfUpdates(_env.HttpClient, _env.Console, _logger, _releasesUrl, manifest.PreviewsEnabled))
        {
            case (false, null):
                return Result.SelfUpdateFailed;
            case (false, _):
                return Result.Success;
            case (true, {} r):
                release = r;
                break;
            default:
                throw ExceptionUtilities.Unreachable;
        }

        var artifactUri = GetReleaseLink(release);
        var archiveName = Path.GetFileName(artifactUri.LocalPath);
        var artifactSigUri = new Uri(artifactUri.ToString() + ".sig");
        var pubKeyUri = new Uri(artifactUri, ReleaseKeyFileName);
        var pubKeySigUri = new Uri(artifactUri, ReleaseKeySigFileName);
        Uri[] downloads = [
            artifactUri,
            artifactSigUri,
            pubKeyUri,
            pubKeySigUri
        ];

        string tempArchiveDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        await DownloadToTempAndDelete(
            downloads,
            downloadDir => VerifyAndExtract(downloadDir, tempArchiveDir, archiveName)
        );
        _logger.Log($"{tempArchiveDir} contents: {string.Join(", ", Directory.GetFiles(tempArchiveDir))}");

        string dnvmTmpPath = Path.Combine(tempArchiveDir, Utilities.DnvmExeName);
        bool success = await ValidateBinary(_env.Console, _logger, dnvmTmpPath);
        if (!success)
        {
            return SelfUpdateFailed;
        }
        var exitCode = RunSelfInstall(_env.Console, _logger, dnvmTmpPath, Utilities.ProcessPath);
        return exitCode == 0 ? Success : SelfUpdateFailed;
    }

    /// <summary>
    /// Unpack the downloaded archive to the given temp directory. The archive is expected to
    /// have the name <param name="archiveName"/> and be located in the <param name="downloadDir"/>.
    /// </summary>
    private async Task VerifyAndExtract(string downloadDir, string tempArchiveDir, string archiveName)
    {
        var relkeyPath = Path.Combine(downloadDir, ReleaseKeyFileName);
        var relkeySigPath = Path.Combine(downloadDir, ReleaseKeySigFileName);

        // Perform the following steps in precisely this order:
        // 1. Verify the release key signature using the root key
        // 2. Verify the archive signature using the release key
        // 3. Extract the archive to the temp directory

        _env.Console.WriteLine("Verifying release key signature...");
        var rootKey = KeyMgr.ParsePublicRootKey(Resources.GetRootPubContent());
        var relKeyBytes = await File.ReadAllBytesAsync(relkeyPath);
        var relKeySig = await File.ReadAllBytesAsync(relkeySigPath);
        bool success = KeyMgr.VerifyReleaseKey(rootKey, relKeyBytes, relKeySig);
        if (success)
        {
            _env.Console.WriteLine("Release key signature OK");
        }
        else
        {
            _env.Console.Error("Release key signature verification FAILED. This is not currently fatal, but it will be in the next release.");
        }

        _env.Console.WriteLine("Verifying archive signature...");
        var archivePath = Path.Combine(downloadDir, archiveName);
        var archiveSigPath = archivePath + ".sig";
        var archiveSig = await File.ReadAllBytesAsync(archiveSigPath);
        var relKeyText = Encoding.UTF8.GetString(relKeyBytes);
        using (var archiveStream = File.OpenRead(archivePath))
        {
            success = KeyMgr.VerifyRelease(relKeyText, archiveStream, archiveSig);
        }

        if (success)
        {
            _env.Console.WriteLine("Archive signature OK");
        }
        else
        {
            _env.Console.Error("Archive signature verification FAILED. This is not currently fatal, but it will be in the next release.");
        }

        _logger.Log("Archive path: " + archivePath);
        string? retMsg = await Utilities.ExtractArchiveToDir(archivePath, tempArchiveDir);
        if (retMsg != null)
        {
            _env.Console.Error("Extraction failed: " + retMsg);
        }
    }

    private static async Task<(bool UpdateAvailable, DnvmReleases.Release? Releases)> CheckForSelfUpdates(
        ScopedHttpClient httpClient,
        IAnsiConsole console,
        Logger logger,
        string releasesUrl,
        bool previewsEnabled)
    {
        console.WriteLine("Checking for updates to dnvm");
        console.WriteLine("Using dnvm releases URL: " + releasesUrl);

        string releasesJson;
        try
        {
            releasesJson = await httpClient.GetStringAsync(releasesUrl);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            console.Error($"Could not fetch releases from URL '{releasesUrl}': {e.Message}");
            return (false, null);
        }

        DnvmReleases releases;
        try
        {
            logger.Log("Releases JSON: " + releasesJson);
            releases = JsonSerializer.Deserialize<DnvmReleases>(releasesJson);
        }
        catch (Exception e)
        {
            console.Error("Could not deserialize Releases JSON: " + e.Message);
            return (false, null);
        }

        var currentVersion = Program.SemVer;
        var release = releases.LatestVersion;
        var newest = SemVersion.Parse(releases.LatestVersion.Version, SemVersionStyles.Strict);
        var preview = releases.LatestPreview is not null
            ? SemVersion.Parse(releases.LatestPreview.Version, SemVersionStyles.Strict)
            : null;

        if (previewsEnabled && preview is not null && newest.ComparePrecedenceTo(preview) < 0)
        {
            console.WriteLine($"Preview version '{preview}' is newer than latest version '{newest}'");
            newest = preview;
            release = releases.LatestPreview;
        }

        if (currentVersion.ComparePrecedenceTo(newest) < 0)
        {
            console.WriteLine("Found newer version: " + newest);
            return (true, release);
        }
        else
        {
            console.WriteLine("No newer version found. Dnvm is up-to-date.");
            return (false, release);
        }
    }

    private Uri GetReleaseLink(DnvmReleases.Release release)
    {
        var rid = Utilities.CurrentRID;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && rid.Arch != Architecture.X64)
        {
            // Dnvm doesn't currently publish non-x64 binaries for windows
            _logger.Log("No non-x64 binaries available for Windows, using x64 instead.");
            rid = rid with {
                Arch = Architecture.X64
            };
        }
        var artifactDownloadLink = release.Artifacts[rid.ToString()];
        _logger.Log("Artifact download link: " + artifactDownloadLink);
        return new(artifactDownloadLink);
    }

    private async Task DownloadToTempAndDelete(Uri[] uris, Func<string, Task> action)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempFolder);
            _logger.Log("Temporary download directory: " + tempFolder);

            foreach (var uri in uris)
            {
                string tempDownloadPath = Path.Combine(tempFolder, Path.GetFileName(uri.LocalPath));
                _logger.Log($"Downloading {uri} to: {tempDownloadPath}");
                using (var tempFile = new FileStream(
                    tempDownloadPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    64 * 1024 /* 64kB */,
                    FileOptions.WriteThrough))
                using (var archiveHttpStream = await _env.HttpClient.GetStreamAsync(uri))
                {
                    // We could have a timeout for downloading the file, but this is heavily dependent
                    // on the user's download speed and if they suspend/resume the process. Instead
                    // we'll rely on the user to cancel the download if it's taking too long.
                    await archiveHttpStream.CopyToAsync(tempFile);
                    await tempFile.FlushAsync();
                }
            }
            await action(tempFolder);
        }
        finally
        {
            Directory.Delete(tempFolder, true);
        }
    }

    public static async Task<bool> ValidateBinary(IAnsiConsole console, Logger? logger, string fileName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Utilities.ChmodExec(fileName);
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
                console.Error($"Could not run downloaded dnvm: {error}");
                return false;
            }
            else if (!output.Contains(usageString))
            {
                console.Error($"Downloaded dnvm did not contain \"{usageString}\": ");
                console.WriteLine(output);
                return false;
            }
            return true;
        }
        return false;
    }

    public static int RunSelfInstall(IAnsiConsole console, Logger logger, string newFileName, string oldPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = newFileName,
            ArgumentList = { "selfinstall", "--update", "--dest-path", $"{oldPath}" }
        };
        if (logger.Enabled)
        {
            psi.ArgumentList.Add("--verbose");
        }
        console.WriteLine("Running selfinstall: " + string.Join(" ", psi.ArgumentList));
        var proc = Process.Start(psi);
        proc!.WaitForExit();
        return proc.ExitCode;
    }
}