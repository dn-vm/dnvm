using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using Zio.FileSystems;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class InstallTests
{
    private readonly TextWriter _log = new StringWriter();
    private readonly Logger _logger;

    public InstallTests()
    {
        _logger = new Logger(_log);
    }

    [Fact]
    public Task LtsInstall() => RunWithServer(async (server, env) =>
    {
        var sdkInstallDir = DnvmEnv.GetSdkPath(DnvmEnv.DefaultSdkDirName);
        var dotnetFile = sdkInstallDir / Utilities.DotnetExeName;
        var dnxFile = sdkInstallDir / Utilities.DnxScriptName;
        Assert.False(env.DnvmHomeFs.FileExists(dotnetFile));
        Assert.False(env.DnvmHomeFs.FileExists(dnxFile));

        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion
        };
        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.True(env.DnvmHomeFs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.DnvmHomeFs.ReadAllText(dotnetFile));
        Assert.True(env.DnvmHomeFs.FileExists(dnxFile));

        var manifest = await Manifest.ReadManifestUnsafe(env);
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);
    });

    [Fact]
    public Task AlreadyInstalled() => RunWithServer(async (server, env) =>
    {
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
        };
        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await Manifest.ReadManifestUnsafe(env);
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);

        var console = (TestConsole)env.Console;
        installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.Contains("already installed", console.Output);
    });

    [Fact]
    public Task AlreadyInstalledForce() => RunWithServer(async (server, env) =>
    {
        var console = new TestConsole();
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            Force = true,
        };
        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await Manifest.ReadManifestUnsafe(env);
        var expectedManifest = Manifest.Empty.AddSdk(MockServer.DefaultLtsVersion);
        Assert.Equal(expectedManifest, manifest);

        console.Clear(home: false);
        installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.DoesNotContain("already installed", console.Output);
    });

    [Fact]
    public Task InstallProgressInConsole() => RunWithServer(async (server, env) =>
    {
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            Verbose = true,
        };
        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        // If the progress bar is displayed, there should be at least two occurrences
        var console = (TestConsole)env.Console;
        var index = console.Output.IndexOf("Downloading SDK");
        Assert.NotEqual(-1, index);
        index = console.Output.Substring(index + 1).IndexOf("Downloading SDK");
        Assert.NotEqual(-1, index);
    });

    [Fact]
    public Task InstallReleaseFromServer() => RunWithServer(async (server, env) =>
    {
        var previewVersion = SemVersion("192.192.192-preview");
        server.RegisterDailyBuild(previewVersion);
        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = previewVersion
        };
        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        var manifest = await Manifest.ReadManifestUnsafe(env);
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
            var previewVersion = SemVersion("192.192.192-preview");
            server.RegisterDailyBuild(previewVersion);
            var options = new DnvmSubCommand.InstallArgs
            {
                SdkVersion = previewVersion
            };
            var installResult = await InstallCommand.Run(env, _logger, options);
            Assert.Equal(InstallCommand.Result.Success, installResult);

            var manifest = await Manifest.ReadManifestUnsafe(env);
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
        var result = await InstallCommand.Run(env, _logger, new InstallCommand.Options()
        {
            SdkVersion = SemVersion("42.42.100")
        });
        Assert.Equal(InstallCommand.Result.Success, result);
    });

    [Fact]
    public Task InstallToCustomDirectory() => RunWithServer(async (server, env) =>
    {
        // Create a custom directory for installation
        var targetFs = new MemoryFileSystem();
        var targetDir = UPath.Root / "dotnet";
        targetFs.CreateDirectory(targetDir);

        var dotnetFile = targetDir / Utilities.DotnetExeName;
        Assert.False(targetFs.FileExists(dotnetFile));
        var dnxFile = targetDir / Utilities.DnxScriptName;
        Assert.False(targetFs.FileExists(dnxFile));

        var options = new InstallCommand.Options
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            TargetDir = (targetDir, targetFs),
        };
        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
        Assert.True(targetFs.FileExists(dotnetFile));
        Assert.True(targetFs.FileExists(dnxFile));
        Assert.Contains(Assets.ArchiveToken, targetFs.ReadAllText(dotnetFile));
    });

    [Fact]
    public Task InstallToRelativeDirectory() => RunWithServer(async (server, env) =>
    {
        // Set up a relative directory path for installation
        var relativePath = "dotnet-sdk";

        // The dir should not exist initially
        var relativeDirPath = UPath.Root / relativePath;
        Assert.False(env.CwdFs.DirectoryExists(relativeDirPath));

        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            Dir = relativePath,
        };

        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        // Verify the directory was created and contains the dotnet executable
        Assert.True(env.CwdFs.DirectoryExists(relativeDirPath));
        var dotnetFile = relativeDirPath / Utilities.DotnetExeName;
        Assert.True(env.CwdFs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.CwdFs.ReadAllText(dotnetFile));
        var dnxFile = relativeDirPath / Utilities.DnxScriptName;
        Assert.True(env.CwdFs.FileExists(dnxFile));

        // Also verify we can install again without errors (should skip)
        var console = (TestConsole)env.Console;
        console.Clear(home: false);
        installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);
    });

    [Fact]
    public Task InstallToNestedRelativeDirectory() => RunWithServer(async (server, env) =>
    {
        // Set up a nested relative directory path for installation
        var relativePath = "nested/dotnet-sdk";

        // The dir should not exist initially
        var relativeDirPath = UPath.Root / "nested" / "dotnet-sdk";
        Assert.False(env.CwdFs.DirectoryExists(relativeDirPath));

        var options = new DnvmSubCommand.InstallArgs
        {
            SdkVersion = MockServer.DefaultLtsVersion,
            Dir = relativePath,
        };

        var installResult = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.Success, installResult);

        // Verify the directory was created and contains the dotnet executable
        Assert.True(env.CwdFs.DirectoryExists(relativeDirPath));
        var dotnetFile = relativeDirPath / Utilities.DotnetExeName;
        Assert.True(env.CwdFs.FileExists(dotnetFile));
        Assert.Contains(Assets.ArchiveToken, env.CwdFs.ReadAllText(dotnetFile));
        var dnxFile = relativeDirPath / Utilities.DnxScriptName;
        Assert.True(env.CwdFs.FileExists(dnxFile));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task AllTopLevelFilesCopied(bool includeDnx) => RunWithServer(async (server, env) =>
    {
        using var tempExtractDir = TestUtils.CreateTempDirectory();

        // Create a custom SDK archive with multiple top-level files
        var dotnetPath = Path.Combine(tempExtractDir.Path, Utilities.DotnetExeName);
        Assets.MakeEchoExe(dotnetPath, Assets.ArchiveToken);

        if (includeDnx)
        {
            var dnxPath = Path.Combine(tempExtractDir.Path, Utilities.DnxScriptName);
            Assets.MakeDnxScript(dnxPath);
        }

        // Create additional top-level files that should be copied
        var additionalFiles = new[]
        {
            "dotnet.dll",
            "hostfxr.dll",
            "LICENSE.txt",
            "ThirdPartyNotices.txt"
        };

        foreach (var fileName in additionalFiles)
        {
            var filePath = Path.Combine(tempExtractDir.Path, fileName);
            File.WriteAllText(filePath, $"Content of {fileName}");
        }

        // Create archive from the temp directory
        var archivePath = Assets.MakeZipOrTarball(tempExtractDir.Path, Path.Combine(TestUtils.ArtifactsTmpDir.FullName, "test-sdk"));

        // Test the ExtractSdkToDir method directly using memory filesystems
        using var tempDir = TestUtils.CreateTempDirectory();
        var physicalFs = new PhysicalFileSystem();
        var tempFs = new SubFileSystem(physicalFs, physicalFs.ConvertPathFromInternal(tempDir.Path));
        var destFs = new MemoryFileSystem();
        var destDir = UPath.Root / "sdk";

        var result = await Utilities.ExtractSdkToDir(
            existingMuxerVersion: null, // No existing version
            runtimeVersion: MockServer.DefaultLtsVersion,
            archivePath: archivePath,
            tempFs: tempFs,
            destFs: destFs,
            destDir: destDir);

        // Verify extraction was successful
        Assert.Null(result);

        // Verify the main dotnet executable was copied
        var installedDotnetFile = destDir / Utilities.DotnetExeName;
        Assert.True(destFs.FileExists(installedDotnetFile));
        Assert.Contains(Assets.ArchiveToken, destFs.ReadAllText(installedDotnetFile));
        var installedDnxFile = destDir / Utilities.DnxScriptName;
        Assert.Equal(includeDnx, destFs.FileExists(installedDnxFile));

        // Verify all additional top-level files were copied
        foreach (var fileName in additionalFiles)
        {
            var installedFile = destDir / fileName;
            Assert.True(destFs.FileExists(installedFile), $"Top-level file {fileName} should have been copied");
            var expectedContent = $"Content of {fileName}";
            var actualContent = destFs.ReadAllText(installedFile);
            Assert.Equal(expectedContent, actualContent);
        }
    });

    [Theory]
    [InlineData("8.0.1", "8.0.100")] // 1-digit patch -> suggest patch*100
    [InlineData("8.0.2", "8.0.200")] // 1-digit non-zero patch -> patch*100
    [InlineData("9.0.0", "9.0.100")] // zero patch -> 100
    [InlineData("9.0.5", "9.0.500")] // patch 5 -> 500
    [InlineData("8.0.10", "8.0.100")] // 2-digit patch -> suggest patch*10
    [InlineData("8.0.21", "8.0.210")] // 2-digit non-zero patch -> patch*10
    [InlineData("9.0.50", "9.0.500")] // patch 50 -> 500
    public Task WarnsOnNonThreeDigitPatch(string versionText, string suggested) => RunWithServer(async (server, env) =>
    {
        // Clear server versions so installation fails (forcing warning path)
        server.ClearVersions();
        var console = (TestConsole)env.Console;
        var version = SemVersion(versionText);
        var options = new DnvmSubCommand.InstallArgs { SdkVersion = version };
        var result = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.UnknownChannel, result); // Should fail lookup
        var output = console.Output;
        Assert.Contains($"Requested SDK version '{version}'", output);
        Assert.Contains($"Did you mean '{suggested}'?", output);
    });

    [Theory]
    [InlineData("8.0.100")]
    [InlineData("8.0.101")]
    [InlineData("9.0.300-preview.1")] // preview with 3-digit patch should not warn
    public Task NoWarningOnThreeDigitPatch(string versionText) => RunWithServer(async (server, env) =>
    {
        server.ClearVersions();
        var console = (TestConsole)env.Console;
        var version = SemVersion(versionText);
        var options = new DnvmSubCommand.InstallArgs { SdkVersion = version };
        var result = await InstallCommand.Run(env, _logger, options);
        Assert.Equal(InstallCommand.Result.UnknownChannel, result);
        var output = console.Output;
        Assert.DoesNotContain("Requested SDK version", output);
        Assert.DoesNotContain("Did you mean", output);
    });

    static SemVersion SemVersion(string version) => Semver.SemVersion.Parse(version, SemVersionStyles.Strict);
}
