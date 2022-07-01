using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serde;
using Serde.Json;

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

    private const string s_dnvmVersionsEndpoint = "https://ja.cksonschuster.com/dnvm/versions/";
    public async Task<string> GetLatestBinaryUri(RID rid, string endpoint = s_dnvmVersionsEndpoint)
    {
        _logger.Info($"Getting latest version from {endpoint}{rid}/latest");
        return await Program.DefaultClient.GetStringAsync($"{endpoint}{rid}/latest");
    }

    public async Task<int> DownloadBinary (string uri, string fileName)
    {
        var response = await Program.DefaultClient.GetAsync(uri);
        using (var fs = File.OpenWrite(fileName)) {
            await response.Content.CopyToAsync(fs);
        }
        _logger.Info($"Downloaded binary to {fileName}");
        return 0;
    }

    public async Task<int> ValidateBinary (string fileName)
    {
        if (Process.Start(new ProcessStartInfo(fileName, "--help") {
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        }) is Process ps)
        {
            await ps.WaitForExitAsync();
            await ps.StandardOutput.ReadToEndAsync();
            if (ps.ExitCode != 0)
                throw new Exception("Downloaded binary failed");
        }
        return 0;
    }

    public int SwapWithRunningFile(string newFileName) 
    {
        if (Process.GetCurrentProcess()?.MainModule?.FileName is not string thisFileName)
            throw new Exception("Could not find path of current process");

        var oldExeTmpFileName = Path.GetTempFileName();
        _logger.Info($"Swapping {thisFileName} with downloaded version at {newFileName}");
        File.Move(thisFileName, oldExeTmpFileName, true);
        File.Move(newFileName, thisFileName, true);
        return 0;
    }

    public async Task<int> Handle_windows()
    {
        if (!_options.Self)
        {
            _logger.Error("update is currently only supported with --self");
            return 0;
        }

        var rid = Program.Rid;

        var binaryDownloadURI = await GetLatestBinaryUri(rid);
        _logger.Info($"Latest binary endpoint: {binaryDownloadURI}");

        string downloadedFileName = Path.GetTempFileName();
        await DownloadBinary(binaryDownloadURI, downloadedFileName);
        await ValidateBinary(downloadedFileName);
        SwapWithRunningFile(downloadedFileName);

        return 0;
    }

    public async Task<int> Handle()
    {
        if (!_options.Self)
        {
            _logger.Error("update is currently only supported with --self");
            return 1;
        }

        string releasesJson = await Program.DefaultClient.GetStringAsync("https://commentout.com/dnvm/releases.json");
        _logger.Info("Releases JSON: " + releasesJson);
        var releases = JsonSerializer.Deserialize<Releases>(releasesJson);
        var rid = Utilities.GetOsName() + "-" + RuntimeInformation.OSArchitecture.ToString().ToLower();
        var artifactDownloadLink = releases.LatestVersion.Artifacts[rid];
        _logger.Info("Artifact download link: " + artifactDownloadLink);

        string tempDownloadPath = Path.GetTempFileName();
        string tempArchiveDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using (var tempFile = new FileStream(
            tempDownloadPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024 /* 64kB */,
            FileOptions.WriteThrough | FileOptions.DeleteOnClose))
        using (var archiveHttpStream = await Program.DefaultClient.GetStreamAsync(artifactDownloadLink))
        {
            await archiveHttpStream.CopyToAsync(tempFile);
            await tempFile.FlushAsync();
            _logger.Info("Extraction directory: " + tempArchiveDir);
            ZipFile.ExtractToDirectory(tempDownloadPath, tempArchiveDir);
        }

        string dnvmTmpPath = Path.Combine(tempArchiveDir, Utilities.ExeName);
        // Replace with File.SetUnixFileMode when available
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = Process.Start("chmod", $"+x \"{dnvmTmpPath}\"");
            await chmod.WaitForExitAsync();
            _logger.Info("chmod return: " + chmod.ExitCode);
        }
        // Run exe and make sure it's OK
        var testProc = Process.Start(new ProcessStartInfo
        {
            FileName = dnvmTmpPath,
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

        try
        {
            string backupPath = Utilities.ProcessPath + ".bak";
            File.Move(Utilities.ProcessPath, backupPath);
            File.Move(dnvmTmpPath, Utilities.ProcessPath, overwrite: false);
            _logger.Log("Process successfully upgraded");
            File.Delete(backupPath);
            return 0;
        }
        catch (Exception e)
        {
            _logger.Error("Couldn't replace existing binary: " + e.Message);
            return 1;
        }
    }
}