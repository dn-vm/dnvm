
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;

namespace Dnvm.Test;

public sealed class RestoreTests
{
    private readonly Logger _logger = new(new StringWriter());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoGlobalJson(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        Assert.Equal(RestoreCommand.Error.NoGlobalJson, restoreResult);
        Assert.Equal("""

        Error: No global.json found in the current directory or any of its parents.


        """.NormalizeLineEndings(), ((TestConsole)env.Console).Output.TrimLines());
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GlobalJsonNoSdkSection(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {}
        """);
        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        Assert.Equal(RestoreCommand.Error.NoSdkSection, restoreResult);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GlobalJsonNoVersion(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {}
        }
        """);
        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        Assert.Equal(RestoreCommand.Error.NoVersion, restoreResult);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactVersionMatch(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", $$"""
        {
            "sdk": {
                "version": "{{MockServer.DefaultLtsVersion}}"
            }
        }
        """);

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 42);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactMatchDirAbove(bool isLocalInstall) => await TestUtils.RunWithServer(UPath.Root / "a" / "b" / "c",
    async (server, env) =>
    {
        var jsonPath = UPath.Root / "global.json";
        env.CwdFs.WriteAllText(jsonPath, $$"""
        {
            "sdk": {
                "version": "{{MockServer.DefaultLtsVersion}}"
            }
        }
        """);

        Assert.False(env.CwdFs.DirectoryExists(UPath.Root / ".dotnet"));
        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 42);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(UPath.Root / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(UPath.Root / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewerPatchVersion(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 43);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));


            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatestPatch(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 43);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatestFeature(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "latestFeature"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 100);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatestMinor(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "latestMinor"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 43, 0);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatestMajor(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "latestMajor"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");
        server.RegisterReleaseVersion(new(43, 0, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = SemVersion.Parse("99.99.99-preview", SemVersionStyles.Strict);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatestMajorNoPrerelease(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "latestMajor",
                "allowPrerelease": false
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");
        server.RegisterReleaseVersion(new(43, 0, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = SemVersion.Parse("43.0.0", SemVersionStyles.Strict);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)

        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PatchAndExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "patch"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 42);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FeatureAndExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "feature"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 42);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MinorAndExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "minor"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 42);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MajorAndExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42",
                "rollForward": "major"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");
        server.RegisterReleaseVersion(new(43, 0, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = SemVersion.Parse("42.42.42", SemVersionStyles.Strict);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PatchNoExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.40",
                "rollForward": "patch"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 43);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FeatureNoExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.40",
                "rollForward": "feature"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 100);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MinorNoExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.40",
                "rollForward": "minor"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 43, 0);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MajorNoExact(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.40",
                "rollForward": "major"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");
        server.RegisterReleaseVersion(new(43, 0, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = SemVersion.Parse("99.99.99-preview", SemVersionStyles.Strict);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MajorNoExactNoPrerelease(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.40",
                "rollForward": "major",
                "allowPrerelease": false
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");
        server.RegisterReleaseVersion(new(43, 0, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = SemVersion.Parse("43.0.0", SemVersionStyles.Strict);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ZipAndExeFiles(bool isLocalInstall) => await TestUtils.RunWithServer(async (server, env) =>
    {
        env.CwdFs.WriteAllText(env.Cwd / "global.json", $$"""
        {
            "sdk": {
                "version": "{{MockServer.DefaultLtsVersion}}"
            }
        }
        """);

        var index = server.ChannelIndexMap[MockServer.DefaultLtsVersion.ToMajorMinor()];
        var sdkComp = index.Releases.Single().Sdk;
        var newSdk = sdkComp with {
            Files = [
                sdkComp.Files[0],
                sdkComp.Files[0] with { Url = sdkComp.Files[0].Url.Replace(Utilities.ZipSuffix, ".exe") }
            ]
        };
        server.ChannelIndexMap[MockServer.DefaultLtsVersion.ToMajorMinor()] = index with {
            Releases = [ index.Releases[0] with { Sdk = newSdk, Sdks = [ newSdk ]} ]
        };

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, _logger, new DnvmSubCommand.RestoreArgs() { Local = isLocalInstall });
        var expectedVersion = new SemVersion(42, 42, 42);
        Assert.Equal(expectedVersion, restoreResult);
        if (isLocalInstall)
        {
            Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        }
        else
        {
            Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(expectedVersion);
            Assert.Equal(expectedManifest, manifest);
        }
    });
}