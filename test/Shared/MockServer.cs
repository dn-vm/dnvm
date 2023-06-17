
using System.Collections.Immutable;
using System.Net;
using System.Text;
using Serde.Json;
using static Dnvm.Utilities;

namespace Dnvm.Test;

public sealed class MockServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly TaskScope _scope;
    public int Port { get; }

    public string PrefixString => $"http://localhost:{Port}/";
    public string DnvmReleasesUrl => PrefixString + "releases.json";
    public string? DnvmPath { get; set; } = null;

    public DotnetReleasesIndex ReleasesIndexJson { get; set; } = new DotnetReleasesIndex
    {
        Releases = ImmutableArray.Create(new DotnetReleasesIndex.Release[] {
            new() {
                LatestRelease = "42.42.42",
                LatestSdk = "42.42.142",
                MajorMinorVersion = "42.42",
                ReleaseType = "lts",
                SupportPhase = "active"
            },
            new() {
                LatestRelease = "99.99.99-preview",
                LatestSdk = "99.99.99-preview",
                MajorMinorVersion = "99.99",
                ReleaseType = "sts",
                SupportPhase = "preview"
            }
        })
    };

    public DnvmReleases DnvmReleases { get; set; }

    public MockServer(TaskScope scope)
    {
        _scope = scope;
        // Generate a temporary port for testing
        while (true)
        {
            Port = Random.Shared.Next(49152, 65535);
            _listener = new HttpListener();
            try
            {
                _listener.Prefixes.Add(PrefixString);
                _listener.Start();
                _ = _scope.Run(WaitForConnection);
                break;
            }
            catch
            {
            }
        }
        DnvmReleases = new()
        {
            LatestVersion = new()
            {
                Version = "24.24.24",
                Artifacts = new() {
                    ["linux-x64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                    ["osx-x64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                    ["win-x64"] = $"{PrefixString}dnvm/dnvm.zip"
                }
            }
        };
    }

    private async Task WaitForConnection()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            if (UrlToHandler.TryGetValue(ctx.Request.Url!.LocalPath.ToLowerInvariant(), out var action))
            {
                action(ctx.Response);
            }
            else
            {
                throw new ArgumentException($"No handler for {ctx.Request.Url.LocalPath}");
            }
        }
    }

    private Dictionary<string, Action<HttpListenerResponse>> UrlToHandler
    {
        get {
            var routes = new Dictionary<string, Action<HttpListenerResponse>>()
            {
                ["/release-metadata/releases-index.json"] = GetReleasesIndexJson,
                ["/releases.json"] = GetReleasesJson,
                [$"/dnvm/dnvm{ZipSuffix}"] = GetDnvm,
            };
            foreach (var r in ReleasesIndexJson.Releases)
            {
                routes[$"/sdk/{r.LatestSdk}/dotnet-sdk-{r.LatestSdk}-{CurrentRID}{ZipSuffix}"] = GetSdk;
            }
            return routes;
        }
    }

    private void GetReleasesIndexJson(HttpListenerResponse response)
    {
        byte[] buffer= Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ReleasesIndexJson));
        response.StatusCode = (int)HttpStatusCode.OK;
        // Get a response stream and write the response to it.
        response.ContentLength64 = buffer.Length;
        var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
        // You must close the output stream.
        output.Close();
    }

    private static void GetSdk(HttpListenerResponse response)
    {
        var f = Assets.SdkArchive;
        var streamLength = f.Length;
        response.ContentLength64 = streamLength;
        f.CopyTo(response.OutputStream);
        response.OutputStream.Close();
    }

    private void GetDnvm(HttpListenerResponse response)
    {
        using var f = DnvmPath is null
            ? Assets.MakeFakeDnvmArchive()
            : File.OpenRead(DnvmPath);
        response.ContentLength64 = f.Length;
        f.CopyTo(response.OutputStream);
        response.OutputStream.Close();
    }

    private void GetReleasesJson(HttpListenerResponse response)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(DnvmReleases));
        // Get a response stream and write the response to it.
        response.ContentLength64 = buffer.Length;
        var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
        // You must close the output stream.
        output.Close();
    }

    public ValueTask DisposeAsync()
    {
        _listener.Close();
        return ValueTask.CompletedTask;
    }
}