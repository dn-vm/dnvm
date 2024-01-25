
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Semver;
using Serde.Json;
using static Dnvm.Utilities;

namespace Dnvm.Test;

public sealed class MockServer : IAsyncDisposable
{
    public static readonly SemVersion DefaultLtsVersion = new SemVersion(42, 42, 42);

    private readonly HttpListener _listener;
    private readonly TaskScope _scope;
    public int Port { get; }
    private volatile bool _disposing = false;

    public string PrefixString => $"http://localhost:{Port}/";
    public string DnvmReleasesUrl => PrefixString + "releases.json";
    public string? DnvmPath { get; set; } = null;

    public DotnetReleasesIndex ReleasesIndexJson { get; set; }

    public DnvmReleases DnvmReleases { get; set; }

    public Dictionary<string, ChannelReleaseIndex> ChannelIndexMap { get; } = new();

    private string GetChannelIndexPath(string majorMinor) => $"release-metadata/{majorMinor}/index.json";
    public string GetChannelIndexUrl(string majorMinor) => PrefixString + GetChannelIndexPath(majorMinor);

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
        RegisterReleaseVersion(DefaultLtsVersion, "lts", "active");
        RegisterReleaseVersion(SemVersion.Parse("99.99.99-preview", SemVersionStyles.Strict), "sts", "preview");
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

    [MemberNotNull(nameof(ReleasesIndexJson))]
    public ChannelReleaseIndex.Release RegisterReleaseVersion(SemVersion version, string releaseType, string supportPhase)
    {
        var majorMinor = version.ToMajorMinor();
        ReleasesIndexJson ??= new DotnetReleasesIndex{ Releases = [ ] };
        ReleasesIndexJson = ReleasesIndexJson with {
            Releases = ReleasesIndexJson.Releases.Add(new() {
                LatestRelease = version.ToString(),
                LatestSdk = version.ToString(),
                MajorMinorVersion = majorMinor,
                ReleaseType = releaseType,
                SupportPhase = supportPhase,
                ChannelReleaseIndexUrl = GetChannelIndexUrl(majorMinor)
            })
        };
        var newRelease = TestUtils.CreateRelease(PrefixString, version);
        newRelease = newRelease with {
            Sdk = newRelease.Sdk with {
                Files = [ new() {
                    Name = $"dotnet-sdk-{CurrentRID}{ZipSuffix}",
                    Hash = "",
                    Rid = CurrentRID.ToString(),
                    Url = $"{PrefixString}sdk/{version}/dotnet-sdk-{CurrentRID}{ZipSuffix}"
                }]
            }
        };
        if (!ChannelIndexMap.TryGetValue(majorMinor, out var index))
        {
            index = new() { Releases = [
                newRelease
            ] };
        }
        else
        {
            index = index.AddRelease(newRelease);
        }
        ChannelIndexMap[majorMinor] = index;
        return newRelease;
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
            catch when (_disposing)
            {
                // Ignore all exceptions while disposing
                break;
            }

            if (UrlToHandler.TryGetValue(ctx.Request.Url!.LocalPath.ToLowerInvariant(), out var action))
            {
                try
                {
                action(ctx.Response);
                }
                catch {}
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
            foreach (var (version, index) in ChannelIndexMap)
            {
                routes["/" + GetChannelIndexPath(version)] = GetChannelIndexJson(index);
            }
            foreach (var r in ReleasesIndexJson.Releases)
            {
                var sdkVersion = SemVersion.Parse(r.LatestSdk, SemVersionStyles.Strict);
                var route = $"/sdk/{sdkVersion}/dotnet-sdk-{sdkVersion}-{CurrentRID}{ZipSuffix}";
                var unversioned = $"/sdk/{sdkVersion}/dotnet-sdk-{CurrentRID}{ZipSuffix}";
                routes[route] = routes[unversioned] = GetSdk(sdkVersion, sdkVersion, sdkVersion, sdkVersion);
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

    private Action<HttpListenerResponse> GetChannelIndexJson(ChannelReleaseIndex index)
    {
        var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(index));
        return (response) =>
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentLength64 = buffer.Length;
            var output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            output.Close();
        };
    }

    private static Action<HttpListenerResponse> GetSdk(
        SemVersion sdkVersion,
        SemVersion runtimeVersion,
        SemVersion aspnetVersion,
        SemVersion winVersion) => response =>
    {
        var f = Assets.GetSdkArchive(sdkVersion, runtimeVersion, aspnetVersion);
        var streamLength = f.Length;
        response.ContentLength64 = streamLength;
        f.CopyTo(response.OutputStream);
        response.OutputStream.Close();
    };

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
        _disposing = true;
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (HttpListenerException) { }
        return ValueTask.CompletedTask;
    }
}