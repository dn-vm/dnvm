
using System.Diagnostics;
using System.Runtime.InteropServices;
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class InstallTests
{
    [Fact]
    public Task LtsInstall() => RunWithServer(async (server, env) =>
    {
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        var dotnetFile = sdkInstallDir / Utilities.DotnetExeName;
        Assert.False(env.DnvmHomeFs.FileExists(dotnetFile));

        var logger = new Logger(new TestConsole());
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.True(env.DnvmHomeFs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.DnvmHomeFs.ReadAllText(dotnetFile));

        var manifest = await env.ReadManifest();
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);
    });

    [Fact]
    public Task AlreadyInstalled() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await env.ReadManifest();
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);

        installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.Contains("already installed", console.Output);
    });

    [Fact]
    public Task AlreadyInstalledForce() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            Force = true,
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await env.ReadManifest();
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);

        console.Clear(home: false);
        installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.DoesNotContain("already installed", console.Output);
    });

    [Fact]
    public Task InstallProgressInConsole() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            Verbose = true,
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        // If the progress bar is displayed, there should be at least two occurrences
        var index = console.Output.IndexOf("Downloading SDK");
        Assert.NotEqual(-1, index);
        index = console.Output.Substring(index + 1).IndexOf("Downloading SDK");
        Assert.NotEqual(-1, index);
    });

    [Fact]
    public Task InstallReleaseFromServer() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var logger = new Logger(console);
        var previewVersion = SemVersion("192.192.192-preview");
        server.RegisterDailyBuild(previewVersion);
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = previewVersion
        };
        var installResult = await InstallCommand.Run(env, logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await env.ReadManifest();
        var expectedManifest = Manifest.Empty.AddSdk(previewVersion);
        Assert.Equal(expectedManifest, manifest);
    });

    [Fact]
    public Task InstallWhileDotnetRunning() => RunWithServer(async (server, env) =>
    {
        Assert.True(env.IsPhysicalDnvmHome);
        var sdkPath = env.RealPath(UPath.Root / DnvmEnv.DefaultSdkDirName.Name);
        Directory.CreateDirectory(sdkPath);
        var dotnetPath = Path.Combine(sdkPath, Utilities.DotnetExeName);
        // Write a script that will run until stopped, to replicate a running dotnet process
        var winSrc = """
        class Program
        {
            public static void Main()
            {
                System.Console.ReadLine();
            }
        }
        """;
        var unixSrc = """
        #!/bin/bash
        read -r line
        """;
        Assets.MakeXplatExe(dotnetPath, unixSrc, winSrc);
        var psi = new ProcessStartInfo
        {
            FileName = dotnetPath,
            WorkingDirectory = sdkPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        var proc = Process.Start(psi)!;
        await Task.Delay(1000);
        Assert.False(proc.HasExited);

        try
        {
            var console = new TestConsole();
            var logger = new Logger(console);
            var previewVersion = SemVersion("192.192.192-preview");
            server.RegisterDailyBuild(previewVersion);
            var options = new DnvmSubCommand.InstallArgs
            {
                SdkVersion = previewVersion
            };
            var installResult = await InstallCommand.Run(env, logger, options);
            Assert.Equal(InstallCommand.Result.Success, installResult);

            var manifest = await env.ReadManifest();
            var expectedManifest = Manifest.Empty.AddSdk(previewVersion);
            Assert.Equal(expectedManifest, manifest);
        }
        finally
        {
            // Close out the running dotnet process
            proc.StandardInput.WriteLine("stop");
            await Task.Delay(1000);
            Assert.True(proc.HasExited);
        }
    });

    [Fact]
    public Task MultipleSdksInRelease() => RunWithServer(async (server, env) =>
    {
        // Tests that if we have multiple SDKs in one release and it still works if try to grab the
        // old one
        server.ReleasesIndexJson = new DotnetReleasesIndex
        {
            ChannelIndices = [
                new DotnetReleasesIndex.ChannelIndex
                {
                    SupportPhase = "active",
                    LatestRelease = "42.42.0",
                    LatestSdk = "42.42.101",
                    MajorMinorVersion = "42.42",
                    ReleaseType = "lts",
                    ChannelReleaseIndexUrl = server.GetChannelIndexUrl("42.42")
                }
            ]
        };
        var sdk100 = new ChannelReleaseIndex.Component
        {
            Version = SemVersion("42.42.100"),
            Files = [
                new ChannelReleaseIndex.File
                {
                    Name = $"dotnet-sdk-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                    Rid = Utilities.CurrentRID.ToString(),
                    Url = $"{server.PrefixString}sdk/42.42.100/dotnet-sdk-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                    Hash = ""
                }
            ]
        };
        var sdk101 = new ChannelReleaseIndex.Component
        {
            Version = SemVersion("42.42.101"),
            Files = [
                new ChannelReleaseIndex.File
                {
                    Name = $"dotnet-sdk-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                    Rid = Utilities.CurrentRID.ToString(),
                    Url = $"{server.PrefixString}sdk/42.42.101/dotnet-sdk-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                    Hash = ""
                }
            ]
        };
        server.ChannelIndexMap["42.42"] = new ChannelReleaseIndex
        {
            Releases = [
                new ChannelReleaseIndex.Release
                {
                    ReleaseVersion = SemVersion("42.42.0"),
                    Sdk = sdk101, // N.B. 101 is the latest but we're looking for 100
                    Sdks = [
                        sdk101,
                        sdk100 // 100 is here
                    ],
                    AspNetCore = new ChannelReleaseIndex.Component
                    {
                        Version = SemVersion("42.42.0"),
                        Files = [
                            new ChannelReleaseIndex.File
                            {
                                Name = $"aspnetcore-runtime-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                                Rid = Utilities.CurrentRID.ToString(),
                                Url = $"{server.PrefixString}aspnetcore-runtime/42.42.0/aspnetcore-runtime-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                                Hash = ""
                            }
                        ]
                    },
                    WindowsDesktop = new ChannelReleaseIndex.Component
                    {
                        Version = SemVersion("42.42.0"),
                        Files = [
                            new ChannelReleaseIndex.File
                            {
                                Name = $"windowsdesktop-runtime-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                                Rid = Utilities.CurrentRID.ToString(),
                                Url = $"{server.PrefixString}windowsdesktop-runtime/42.42.0/windowsdesktop-runtime-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                                Hash = ""
                            }
                        ]
                    },
                    Runtime = new ChannelReleaseIndex.Component
                    {
                        Version = SemVersion("42.42.0"),
                        Files = [
                            new ChannelReleaseIndex.File
                            {
                                Name = $"dotnet-runtime-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                                Rid = Utilities.CurrentRID.ToString(),
                                Url = $"{server.PrefixString}runtime/42.42.0/dotnet-runtime-{Utilities.CurrentRID}{Utilities.ZipSuffix}",
                                Hash = ""
                            }
                        ]
                    }
                }
            ]
        };

        var console = new TestConsole();
        var logger = new Logger(console);
        var result = await InstallCommand.Run(env, logger, new InstallCommand.Options()
        {
            SdkVersion = SemVersion("42.42.100")
        });
        Assert.Equal(InstallCommand.Result.Success, result);
    });

    static SemVersion SemVersion(string version) => Semver.SemVersion.Parse(version, SemVersionStyles.Strict);
}