
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using Dnvm.Signing;
using Semver;
using Serde.Json;
using static Dnvm.Utilities;

namespace Dnvm.Test;

public sealed class MockServer : IAsyncDisposable
{
    public static readonly SemVersion DefaultLtsVersion = new SemVersion(42, 42, 42);
    public static readonly SemVersion DefaultPreviewVersion = SemVersion.Parse("99.99.99-preview", SemVersionStyles.Strict);

    private readonly HttpListener _listener;
    private readonly TaskScope _scope;
    public int Port { get; }
    private volatile bool _disposing = false;
    private readonly List<SemVersion> _dailyBuilds = new();

    public string PrefixString => $"http://localhost:{Port}/";
    public string DnvmReleasesUrl => PrefixString + "releases.json";

    private Lazy<string> _archivePath = new(() => {
        var path = Assets.MakeFakeDnvmArchive();
        return path;
    });

    public Lazy<DnvmArtifacts> Artifacts { get; }

    public sealed partial record DnvmArtifacts(
        string ArchivePath,
        byte[] ArchiveSig,
        string PubKey,
        byte[] PubKeySig
    );

    public DotnetReleasesIndex ReleasesIndexJson { get; set; }

    public DnvmReleases DnvmReleases { get; set; }

    /// <summary>
    /// Map from major.minor version to the channel index for that version.
    /// </summary>
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
        RegisterReleaseVersion(DefaultPreviewVersion, "sts", "preview");
        DnvmReleases = new(new(
            Version: "24.24.24",
            Artifacts: new() {
                ["linux-x64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                ["linux-arm64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                ["osx-x64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                ["osx-arm64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                ["win-x64"] = $"{PrefixString}dnvm/dnvm.zip"
        }))
        {
            LatestPreview = new(
                Version: "99.99.99-preview",
                Artifacts: new() {
                    ["linux-x64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                    ["linux-arm64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                    ["osx-x64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                    ["osx-arm64"] = $"{PrefixString}dnvm/dnvm.tar.gz",
                    ["win-x64"] = $"{PrefixString}dnvm/dnvm.zip"
            })
        };
        Artifacts = new(() =>
        {
            var path = _archivePath.Value;
            var (privKey, pubKey) = KeyMgr.GenerateReleaseKey();
            using var archiveStream = File.OpenRead(path);
            var archiveSig = KeyMgr.SignRelease(privKey, archiveStream);
            // This is a placeholder for the public key signature, which is not used in tests.
            var pubKeySig = new byte[archiveSig.Length];
            return new DnvmArtifacts(
                path,
                archiveSig,
                pubKey,
                pubKeySig
            );
        });
    }

    public void ClearVersions()
    {
        ChannelIndexMap.Clear();
        ReleasesIndexJson = DotnetReleasesIndex.Empty;
    }

    [MemberNotNull(nameof(ReleasesIndexJson))]
    public ChannelReleaseIndex.Release RegisterReleaseVersion(SemVersion version, string releaseType, string supportPhase)
    {
        var majorMinor = version.ToMajorMinor();
        ReleasesIndexJson ??= DotnetReleasesIndex.Empty;
        var channel = ReleasesIndexJson.ChannelIndices.SingleOrDefault(c => c.MajorMinorVersion == majorMinor);
        if (channel is null)
        {
            ReleasesIndexJson = ReleasesIndexJson with
            {
                ChannelIndices = ReleasesIndexJson.ChannelIndices.Add(new()
                {
                    LatestRelease = version.ToString(),
                    LatestSdk = version.ToString(),
                    MajorMinorVersion = majorMinor,
                    ReleaseType = releaseType,
                    SupportPhase = supportPhase,
                    ChannelReleaseIndexUrl = GetChannelIndexUrl(majorMinor)
                })
            };
        }
        else if (SemVersion.Parse(channel.LatestRelease, SemVersionStyles.Strict).ComparePrecedenceTo(version) < 0)
        {
            var newChannel = channel with
            {
                LatestRelease = version.ToString(),
                LatestSdk = version.ToString()
            };
            ReleasesIndexJson = ReleasesIndexJson with
            {
                ChannelIndices = ReleasesIndexJson.ChannelIndices.Replace(channel, newChannel)
            };
        }
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

    public void RegisterDailyBuild(SemVersion version)
    {
        _dailyBuilds.Add(version);
    }

    public void SetArchivePath(string path)
    {
        _archivePath = new(() => path);
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
                catch { }
            }
            else
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                ctx.Response.Close();
            }
        }
    }

    private Dictionary<string, Action<HttpListenerResponse>> UrlToHandler
    {
        get {
            var routes = new Dictionary<string, Action<HttpListenerResponse>>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["/release-metadata/releases-index.json"] = GetReleasesIndexJson,
                ["/releases.json"] = GetReleasesJson,
                [$"/dnvm/dnvm{ZipSuffix}"] = GetDnvm,
                [$"/dnvm/dnvm{ZipSuffix}.sig"] = r => WriteOk(r, Artifacts.Value.ArchiveSig),
                [$"/dnvm/relkeys.pub"] = r => WriteOk(r, Encoding.UTF8.GetBytes(Artifacts.Value.PubKey)),
                [$"/dnvm/relkeys.pub.sig"] = r => WriteOk(r, Artifacts.Value.PubKeySig),
            };
            foreach (var (version, index) in ChannelIndexMap)
            {
                routes["/" + GetChannelIndexPath(version)] = GetChannelIndexJson(index);
                foreach (var release in index.Releases)
                {
                    foreach (var sdk in release.Sdks)
                    {
                        routes[$"/sdk/{sdk.Version}/dotnet-sdk-{CurrentRID}{ZipSuffix}"] = GetSdk(
                            sdk.Version,
                            release.Runtime.Version,
                            release.AspNetCore.Version,
                            release.WindowsDesktop.Version
                        );
                    }
                }
            }
            foreach (var channelIndex in ReleasesIndexJson.ChannelIndices)
            {
                var sdkVersion = SemVersion.Parse(channelIndex.LatestSdk, SemVersionStyles.Strict);
                var route = $"/sdk/{sdkVersion}/dotnet-sdk-{sdkVersion}-{CurrentRID}{ZipSuffix}";
                var unversioned = $"/sdk/{sdkVersion}/dotnet-sdk-{CurrentRID}{ZipSuffix}";
                routes[route] = routes[unversioned] = GetSdk(sdkVersion, sdkVersion, sdkVersion, sdkVersion);
            }
            foreach (var v in _dailyBuilds)
            {
                var productCommit = MakeProductCommit(v);
                routes[$"/sdk/{v}/productCommit-{CurrentRID}.json"] = WriteJson(productCommit);
                var route = $"/sdk/{v}/dotnet-sdk-{v}-{CurrentRID}{ZipSuffix}";
                var unversioned = $"/sdk/{v}/dotnet-sdk-{CurrentRID}{ZipSuffix}";
                routes[route] = routes[unversioned] = GetSdk(v, v, v, v);
            }
            return routes;
        }
    }

    private static string MakeProductCommit(SemVersion version) => $$"""
{
    "installer": { "version": "{{version}}" },
    "sdk": { "version": "{{version}}" },
    "runtime": { "version": "{{version}}" },
    "aspnetcore": { "version": "{{version}}" },
    "windowsdesktop": { "version": "{{version}}" }
}
""";

    private void GetReleasesIndexJson(HttpListenerResponse response)
        => WriteJson(JsonSerializer.Serialize(ReleasesIndexJson))(response);

    private static Action<HttpListenerResponse> WriteJson(string json)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        return (response) => WriteOk(response, buffer);
    }

    private Action<HttpListenerResponse> GetChannelIndexJson(ChannelReleaseIndex index)
        => WriteJson(JsonSerializer.Serialize(index));

    private static Action<HttpListenerResponse> GetSdk(
        SemVersion sdkVersion,
        SemVersion runtimeVersion,
        SemVersion aspnetVersion,
        SemVersion winVersion) => response =>
    {
        using var f = Assets.GetSdkArchive(sdkVersion, runtimeVersion, aspnetVersion);
        WriteOk(response, f);
    };

    private void GetDnvm(HttpListenerResponse response)
    {
        using var f = File.OpenRead(Artifacts.Value.ArchivePath);
        WriteOk(response, f);
    }

    private static void WriteOk(HttpListenerResponse response, Stream s)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentLength64 = s.Length;
        s.CopyTo(response.OutputStream);
        response.OutputStream.Close();
    }

    private static void WriteOk(HttpListenerResponse response, byte[] content)
        => WriteOk(response, new MemoryStream(content));

    private void GetReleasesJson(HttpListenerResponse response) => WriteJson(JsonSerializer.Serialize(DnvmReleases))(response);

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