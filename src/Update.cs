using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde;
using Serde.Json;

namespace Dnvm;

public sealed partial class Update
{
    private readonly Logger _logger;
    private readonly Command.UpdateOptions _options;

    public Update(Logger logger, Command.UpdateOptions options)
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
        if (!_options.Self)
        {
            _logger.Error("update is currently only supported with --self");
            return 1;
        }

        if (Assembly.GetEntryAssembly()?.Location != "")
        {
            Console.WriteLine("Cannot self-update: the current executable is not deployed as a single file.");
            return 1;
        }

        string artifactDownloadLink = await GetReleaseLink();

        string tempArchiveDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Func<string, Task> handleDownload = async tempDownloadPath =>
        {
            _logger.Info("Extraction directory: " + tempArchiveDir);
            await Utilities.ExtractArchiveToDir(tempDownloadPath, tempArchiveDir);
        };

        await DownloadBinaryToTempAndDelete(artifactDownloadLink, handleDownload);
        _logger.Info($"Downloaded binary to {tempArchiveDir}");

        string dnvmTmpPath = Path.Combine(tempArchiveDir, Utilities.ExeName);
        bool success =
            await ValidateBinary(dnvmTmpPath) &&
            SwapWithRunningFile(dnvmTmpPath);
        return success ? 0 : 1;
    }


    [GenerateSerialize, GenerateDeserialize]
    public partial record struct Releases(Release LatestVersion);

    [GenerateSerialize, GenerateDeserialize]
    public partial record struct Release(
        string Version,
        Dictionary<string, string> Artifacts);

    public async Task<string> GetReleaseLink()
    {
        var releasesUrl = _options.ReleasesUrl ?? "https://agocke.github.io/dnvm/releases.json";
        string releasesJson = await Program.DefaultClient.GetStringAsync(releasesUrl);
        _logger.Info("Releases JSON: " + releasesJson);
        var releases = JsonSerializer.Deserialize<Releases>(releasesJson);
        var rid = Utilities.CurrentRID.ToString();
        var artifactDownloadLink = releases.LatestVersion.Artifacts[rid];
        _logger.Info("Artifact download link: " + artifactDownloadLink);
        return artifactDownloadLink;
    }

    private static async Task DownloadBinaryToTempAndDelete(string uri, Func<string, Task> action)
    {
        string tempDownloadPath = Path.GetTempFileName();
        using var tempFile = new FileStream(
            tempDownloadPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024 /* 64kB */,
            FileOptions.WriteThrough | FileOptions.DeleteOnClose);
        using var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(uri);
        await archiveHttpStream.CopyToAsync(tempFile);
        await tempFile.FlushAsync();
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
            if (ps.ExitCode != 0 || !output.Contains("usage: "))
            {
                _logger.Error("Could not run downloaded dnvm");
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
            File.Move(Utilities.ProcessPath, backupPath);
            File.Move(newFileName, Utilities.ProcessPath, overwrite: false);
            _logger.Log("Process successfully upgraded");
            File.Delete(backupPath);
            return true;
        }
        catch (Exception e)
        {
            _logger.Error("Couldn't replace existing binary: " + e.Message);
            return false;
        }
    }
}