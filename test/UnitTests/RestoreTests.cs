
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;

namespace Dnvm.Test;

public sealed class RestoreTests
{
    [Fact]
    public async Task NoGlobalJson() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(RestoreCommand.Error.NoGlobalJson, restoreResult);
        Assert.Equal("""
        Error: No global.json found in the current directory or any of its parents.

        """.NormalizeLineEndings(), console.Output.TrimLines());
    });

    [Fact]
    public async Task GlobalJsonNoSdkSection() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {}
        """);
        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(RestoreCommand.Error.NoSdkSection, restoreResult);
    });

    [Fact]
    public async Task GlobalJsonNoVersion() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {}
        }
        """);
        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(RestoreCommand.Error.NoVersion, restoreResult);
    });

    [Fact]
    public async Task ExactVersionMatch() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
        env.CwdFs.WriteAllText(env.Cwd / "global.json", $$"""
        {
            "sdk": {
                "version": "{{MockServer.DefaultLtsVersion}}"
            }
        }
        """);

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 42), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task ExactMatchDirAbove() => await TestUtils.RunWithServer(UPath.Root / "a" / "b" / "c",
    async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
        var jsonPath = UPath.Root / "global.json";
        env.CwdFs.WriteAllText(jsonPath, $$"""
        {
            "sdk": {
                "version": "{{MockServer.DefaultLtsVersion}}"
            }
        }
        """);

        Assert.False(env.CwdFs.DirectoryExists(UPath.Root / ".dotnet"));
        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 42), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(UPath.Root / ".dotnet"));
    });

    [Fact]
    public async Task NewerPatchVersion() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.42"
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 43), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task LatestPatch() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 43), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task LatestFeature() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 100), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task LatestMinor() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 43, 0), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task LatestMajor() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(SemVersion.Parse("99.99.99-preview", SemVersionStyles.Strict), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task LatestMajorNoPrerelease() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(SemVersion.Parse("43.0.0", SemVersionStyles.Strict), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task PatchAndExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 42), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task FeatureAndExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 42), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task MinorAndExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 42), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task MajorAndExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(SemVersion.Parse("42.42.42", SemVersionStyles.Strict), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task PatchNoExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 43), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task FeatureNoExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 42, 100), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task MinorNoExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(new SemVersion(42, 43, 0), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task MajorNoExact() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
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

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(SemVersion.Parse("99.99.99-preview", SemVersionStyles.Strict), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });

    [Fact]
    public async Task MajorNoExactNoPrerelease() => await TestUtils.RunWithServer(async (server, env) =>
    {
        var logger = new Logger(new TestConsole());
        env.CwdFs.WriteAllText(env.Cwd / "global.json", """
        {
            "sdk": {
                "version": "42.42.40",
                "rollForward": "major"
                "allowPrerelease": false
            }
        }
        """);
        server.RegisterReleaseVersion(new(42, 42, 100), "lts", "active");
        server.RegisterReleaseVersion(new(42, 42, 43), "lts", "active");
        server.RegisterReleaseVersion(new(42, 43, 0), "lts", "active");
        server.RegisterReleaseVersion(new(43, 0, 0), "lts", "active");

        Assert.False(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));

        var restoreResult = await RestoreCommand.Run(env, logger);
        Assert.Equal(SemVersion.Parse("43.0.0", SemVersionStyles.Strict), restoreResult);
        Assert.True(env.CwdFs.DirectoryExists(env.Cwd / ".dotnet"));
    });
}