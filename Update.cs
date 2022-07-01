using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Serde;
using Serde.Json;
using static Dnvm.Utilities;

namespace Dnvm;

sealed partial class Update
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

    [GenerateDeserialize]
    [SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
    partial struct Releases
    {
        public Release LatestVersion { get; init; }
    }

    [GenerateDeserialize]
    [SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
    partial struct Release
    {
        public string Version { get; init; }

        public Dictionary<string, string> Artifacts { get; init; }
    }

    public async Task<string> GetLatestReleaseUri(string releasesJsonUri = "https://commentout.com/dnvm/releases.json")
    {
        string releasesJson = await Program.DefaultClient.GetStringAsync(releasesJsonUri);
        _logger.Info("Releases JSON: " + releasesJson);
        var releases = JsonSerializer.Deserialize<Releases>(releasesJson);
        var rid = Program.Rid;
        var artifactDownloadLink = releases.LatestVersion.Artifacts[rid.ToString()];
        _logger.Info("Artifact download link: " + artifactDownloadLink);
        return artifactDownloadLink;
    }

    public async Task<string> DownloadBinary(string uri)
    {
        string tempDownloadPath = Path.GetTempFileName();
        string tempArchiveDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using (var tempFile = new FileStream(
            tempDownloadPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024 /* 64kB */,
            FileOptions.WriteThrough))
        using (var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(uri))
        {
            await archiveHttpStream.CopyToAsync(tempFile);
            await tempFile.FlushAsync();
        }
        _logger.Info("Extraction directory: " + tempArchiveDir);
        ZipFile.ExtractToDirectory(tempDownloadPath, tempArchiveDir);
        File.Delete(tempDownloadPath);
        return Path.Combine(tempArchiveDir, Utilities.ExeName);
    }

    public async Task<int> MakeExecutable(string path)
    {
        // Replace with File.SetUnixFileMode when available
        if (Program.Rid.Os == Os.win)
            return 0;
        if (Process.Start("chmod", $"+x \"{path}\"") is not Process chmod)
        {
            return 1;
        }
        await chmod.WaitForExitAsync();
        _logger.Info("chmod return: " + chmod.ExitCode);
        return chmod.ExitCode;
    }
    public async Task<int> ValidateBinary(string dnvmPath)
    {
        // Run exe and make sure it's OK
        await MakeExecutable(dnvmPath);
        var testProc = Process.Start(new ProcessStartInfo
        {
            FileName = dnvmPath,
            ArgumentList = { "--help" },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (testProc is not null)
        {
            await testProc.WaitForExitAsync();
        }
        if (testProc?.ExitCode != 0)
        {
            _logger.Error("Could not run downloaded dnvm");
            return 1;
        }
        return 0;
    }

    public int SwapBinaries(string dnvmTmpPath, string runningFile)
    {
        try
        {
            string backupPath = runningFile + ".bak";
            File.Move(runningFile, backupPath);
            File.Move(dnvmTmpPath, runningFile, overwrite: false);
            _logger.Log("Process successfully upgraded");
            if (Program.Rid.Os == Os.win)
                // For Windows we can't delete the running file, so just move it to %TEMP% for now
                File.Move(backupPath, Path.GetTempFileName());
            else
                File.Delete(backupPath);
            return 0;
        }
        catch (Exception e)
        {
            _logger.Error("Couldn't replace existing binary: " + e.Message);
            return 1;
        }
    }

    public async Task<int> Handle()
    {
        if (!_options.Self)
        {
            _logger.Error("update is currently only supported with --self");
            return 1;
        }
        string artifactDownloadLink = await GetLatestReleaseUri();
        string dnvmTmpPath = await DownloadBinary(artifactDownloadLink);
        await ValidateBinary(dnvmTmpPath);
        return SwapBinaries(dnvmTmpPath, Utilities.ProcessPath);
    }
}