
using System.Diagnostics;
using System.Threading.Tasks;
using Serde;
using Serde.Json;
using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using static System.Environment;
using System.Net.Sockets;
using dnvm;
using System.Runtime.CompilerServices;

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
    partial struct LatestReleaseResponse
    {
        public string assets_url { get; init; }
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

    public async Task<int> Handle()
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
}