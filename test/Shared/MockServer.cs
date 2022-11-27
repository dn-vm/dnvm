
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using static Dnvm.Utilities;

namespace Dnvm.Test;

public sealed class MockServer : IDisposable
{
    private readonly HttpListener _listener;
    private Task _task;
    public int Port { get; }

    public string PrefixString => $"http://localhost:{Port}/";
    private const string VersionString = "42.42.42";

    public string ReleasesIndexJson { get; set; } = """
{
    "releases-index": [
        {
            "channel-version": "42.42",
            "latest-release": "42.42.42",
            "latest-sdk": "42.42.42",
            "release-type" : "lts",
            "support-phase": "active"
        }
    ]
}
""";

    public MockServer()
    {
        // Generate a temporary port for testing
        while (true)
        {
            Port = Random.Shared.Next(49152, 65535);
            _listener = new HttpListener();
            try
            {
                _listener.Prefixes.Add(PrefixString);
                _listener.Start();
                _task = Task.Run(WaitForConnection);
                return;
            }
            catch
            {
            }
        }
    }

    private async Task WaitForConnection()
    {
        var ctx = await _listener.GetContextAsync();
        if (UrlToHandler.TryGetValue(ctx.Request.Url!.LocalPath.ToLowerInvariant(), out var action))
        {
            action(ctx.Response);
        }
        else
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        _task = Task.Run(WaitForConnection);
    }

    private Dictionary<string, Action<HttpListenerResponse>> UrlToHandler => new()
    {
        ["/release-metadata/releases-index.json"] = GetReleasesIndexJson,
        ["/sdk/lts/latest.version"] = GetLatestVersionUrl,
        [$"/sdk/{VersionString}/dotnet-sdk-{VersionString}-{CurrentRID}.{ZipSuffix}"] = GetSdk,
        ["/releases.json"] = GetReleasesJson,
        [$"/dnvm/dnvm.{ZipSuffix}"] = GetDnvm,
    };

    private void GetReleasesIndexJson(HttpListenerResponse response)
    {
        byte[] buffer;
        if (ReleasesIndexJson is null)
        {
            buffer = Encoding.UTF8.GetBytes(nameof(ReleasesIndexJson) + " property must be set");
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        else
        {
            buffer = Encoding.UTF8.GetBytes(ReleasesIndexJson);
            response.StatusCode = (int)HttpStatusCode.OK;
        }
        // Get a response stream and write the response to it.
        response.ContentLength64 = buffer.Length;
        var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
        // You must close the output stream.
        output.Close();
    }

    private static void GetLatestVersionUrl(HttpListenerResponse response)
    {
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(VersionString);
        // Get a response stream and write the response to it.
        response.ContentLength64 = buffer.Length;
        var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
        // You must close the output stream.
        output.Close();
    }

    private static void GetSdk(HttpListenerResponse response)
    {
        using var f = Assets.GetOrMakeFakeSdkArchive();
        var streamLength = f.Length;
        response.ContentLength64 = streamLength;
        f.CopyTo(response.OutputStream);
        response.OutputStream.Close();
    }

    private static void GetDnvm(HttpListenerResponse response)
    {
        using var f = Assets.MakeFakeDnvmArchive();
        response.ContentLength64 = f.Length;
        f.CopyTo(response.OutputStream);
        response.OutputStream.Close();
    }

    private void GetReleasesJson(HttpListenerResponse response)
    {
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes($$"""
{
    "latestVersion":{
        "version":"{{VersionString}}",
        "artifacts":{
            "linux-x64":"{{PrefixString}}dnvm/dnvm.tar.gz",
            "osx-x64":"{{PrefixString}}dnvm/dnvm.tar.gz",
            "win-x64":"{{PrefixString}}dnvm/dnvm.zip"
        }
    }
}
""");
        // Get a response stream and write the response to it.
        response.ContentLength64 = buffer.Length;
        var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
        // You must close the output stream.
        output.Close();
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}