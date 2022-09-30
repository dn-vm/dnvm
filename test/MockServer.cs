
using System.Net;
using System.Runtime.InteropServices;
using static Dnvm.Utilities;

namespace Dnvm.Test;

internal sealed class MockServer : IDisposable
{
    private readonly HttpListener _listener;
    public int Port { get; }

    private string PrefixString => $"http://localhost:{Port}/";
    private const string VersionString = "42.42.42";

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
                _ = Task.Run(WaitForConnection);
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
        if (UrlToHandler.TryGetValue(ctx.Request.Url!.LocalPath, out var action))
        {
            action(ctx.Response);
        }
        _ = Task.Run(WaitForConnection);
    }

    private static readonly Dictionary<string, Action<HttpListenerResponse>> UrlToHandler = new()
    {
        ["/Sdk/LTS/latest.version"] = GetLatestVersionUrl,
        [$"/Sdk/{VersionString}/dotnet-sdk-{VersionString}-{CurrentRID}.{ZipSuffix}"] = GetSdk
    };

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

    public void Dispose()
    {
        _listener.Stop();
    }
}