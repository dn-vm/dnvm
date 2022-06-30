
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

namespace Dnvm;

sealed partial class Update
{
    private readonly Logger _logger;
    private readonly Command.UpdateOptions _options;
    private static readonly HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });

    public Update(Logger logger, Command.UpdateOptions options)
    {
        _logger = logger;
        _options = options;
    }

    internal record struct SelfVersionOptions()

    [GenerateDeserialize]
    partial struct LatestReleaseResponse
    {
        public string assets_url { get; init; }
    }

    public async Task<string> GetBinaryUri (string endpoint)
    {
        return await Program.DefaultClient.GetStringAsync(endpoint);
    }

    public async Task<string> DownloadAndExchange(string uri)
    {
        var response = await Program.DefaultClient.GetAsync(uri);
        var downloadedTmpFileName = Path.GetTempFileName();
        using (var fs = File.OpenWrite(downloadedTmpFileName)) {
            await response.Content.CopyToAsync(fs);
        }
        if (Process.Start(new ProcessStartInfo(downloadedTmpFileName, "--help")) is Process ps)
        {
            ps.WaitForExit();
            if (ps.ExitCode != 0)
                throw new Exception("Downloaded binary failed");
        }
        if (Path.GetDirectoryName(Process.GetCurrentProcess()?.MainModule?.FileName) is not string thisFileName)
            throw new Exception("Could not find path of current process");

        var oldExeTmpFileName = Path.GetTempFileName();
        File.Move(thisFileName, oldExeTmpFileName);
        File.Move(downloadedTmpFileName, thisFileName);
        return "";
    }

    public async Task<int> Handle()
    {
        if (!_options.Self)
        {
            _logger.Error("update is currently only supported with --self");
            return 0;
        }

        string versionsEndpoint = "https://ja.cksonschuster.com/dnvm/versions/";

        string? osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ?
                RuntimeInformation.RuntimeIdentifier.Contains("musl") ? "linux-musl"
                : "linux"
            : null;

        if (osName is null)
        {
            Console.WriteLine("Could not determine current OS");
            return 1;
        }

        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
        
        var endpoint = versionsEndpoint + "/" + osName + "-" + arch;
        await DownloadAndExchange(await GetBinaryUri(endpoint));


        _logger.Error("Not currently supported");
        return 0;
    }
}