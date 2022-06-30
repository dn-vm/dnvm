
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

    [GenerateDeserialize]
    partial struct LatestReleaseResponse
    {
        public string assets_url { get; init; }
    }

    public async GetBinaryUri (string endpoint)
    {
        var requestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            endpoint);
        var response = await s_noRedirectClient.SendAsync(requestMessage);
    }

    public async Task<Path> DownloadBinary(string uri)
    {

    }

    public Task<int> Handle()
    {
        if (!_options.Self)
        {
            _logger.Error("update is currently only supported with --self");
            return Task.FromResult(1);
        }

        string versionsEndpoint = "https://agocke.github.io/dnvm/versions/releases.json";

        string? osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ?
                RuntimeInformation.RuntimeIdentifier.Contains("musl") ? "linux-musl"
                : "linux"
            : null;

        if (osName is null)
        {
            Console.WriteLine("Could not determine current OS");
            return new Task<int> (() => 1);
        }

        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();



        _logger.Error("Not currently supported");
        return Task.FromResult(1);
    }
}