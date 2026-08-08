using System.Linq;
using StaticCs.Collections;

using System.Net.Security;
using Semver;
using Spectre.Console.Testing;
using Xunit;
using Zio;
using static Dnvm.Test.TestUtils;

namespace Dnvm.Test;

public sealed class UninstallTests
{
    private readonly Logger _logger = new Logger(new StringWriter());

    [Fact]
    public Task LtsAndPreview() => RunWithServer(async (server, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Preview(),
            SdkDir = new("preview")
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        var ltsVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[0].LatestSdk, SemVersionStyles.Strict);
        var previewVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[1].LatestSdk, SemVersionStyles.Strict);
        var expectedManifest = Manifest.Empty
            .AddSdk(ltsVersion, new Channel.Latest(), DnvmEnv.DefaultSdkDirName)
            .AddSdk(previewVersion, new Channel.Preview(), new SdkDirName("preview"));
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Equal(expectedManifest, manifest);
        var unResult = await UninstallCommand.Run(env, _logger, ltsVersion);
        Assert.Equal(0, unResult);
        manifest = await Manifest.ReadManifestUnsafe(env);
        var previewOnly = Manifest.Empty
            .AddSdk(previewVersion, new Channel.Preview(), new SdkDirName("preview"));
        previewOnly = previewOnly with {
            RegisteredChannels = manifest.RegisteredChannels
        };
        Assert.Equal(previewOnly, manifest);

        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.NETCore.App" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.AspNetCore.App" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "shared" / "Microsoft.AspNetCore.App" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "host" / "fxr" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "packs" / $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "packs" / "Microsoft.NETCore.App.Ref" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "packs" / "Microsoft.AspNetCore.App.Ref" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "packs" / "Microsoft.WindowsDesktop.App.Ref" / ltsVersion.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(UPath.Root / "dn" / "sdk-manifests" / ltsVersion.ToFeatureBand()));

        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "shared" / "Microsoft.NETCore.App" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "shared" / "Microsoft.AspNetCore.App" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "host" / "fxr" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "packs" / $"Microsoft.NETCore.App.Host.{Utilities.CurrentRID}" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "packs" / "Microsoft.NETCore.App.Ref" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "packs" / "Microsoft.AspNetCore.App.Ref" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "packs" / "Microsoft.WindowsDesktop.App.Ref" / previewVersion.ToString()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "sdk-manifests" / previewVersion.ToFeatureBand()));
        Assert.True(env.DnvmHomeFs.DirectoryExists(UPath.Root / "preview" / "templates" / previewVersion.ToString()));
    });

    [Fact]
    public Task DirectorySpecificUninstallKeepsOtherCopy() => RunWithServer(async (server, env) =>
    {
        var version = MockServer.DefaultLtsVersion;
        var previewDir = new SdkDirName("preview");

        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs
            {
                SdkVersion = version,
            }));
        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs
            {
                SdkVersion = version,
                SdkDir = previewDir,
            }));

        using (var @lock = await ManifestLock.Acquire(env))
        {
            var manifest = await @lock.ReadManifest(env);
            manifest = manifest with
            {
                RegisteredChannels =
                [
                    new RegisteredChannel
                    {
                        ChannelName = new Channel.Latest(),
                        SdkDirName = DnvmEnv.DefaultSdkDirName,
                        InstalledSdkVersions = [version],
                    },
                    new RegisteredChannel
                    {
                        ChannelName = new Channel.Latest(),
                        SdkDirName = previewDir,
                        InstalledSdkVersions = [version],
                    },
                ]
            };
            await @lock.WriteManifest(env, manifest);
        }

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, version, previewDir));

        var updated = await Manifest.ReadManifestUnsafe(env);
        Assert.Contains(updated.InstalledSdks,
            sdk => sdk.SdkVersion == version && sdk.SdkDirName == DnvmEnv.DefaultSdkDirName);
        Assert.DoesNotContain(updated.InstalledSdks,
            sdk => sdk.SdkVersion == version && sdk.SdkDirName == previewDir);
        Assert.Contains(updated.RegisteredChannels,
            channel => channel.SdkDirName == DnvmEnv.DefaultSdkDirName
                && channel.InstalledSdkVersions.Contains(version));
        Assert.Contains(updated.RegisteredChannels,
            channel => channel.SdkDirName == previewDir
                && !channel.InstalledSdkVersions.Contains(version));

        Assert.True(env.DnvmHomeFs.DirectoryExists(
            UPath.Root / DnvmEnv.DefaultSdkDirName.Name / "sdk" / version.ToString()));
        Assert.False(env.DnvmHomeFs.DirectoryExists(
            UPath.Root / previewDir.Name / "sdk" / version.ToString()));
    });

    [Fact]
    public Task UninstallMessage() => RunWithServer(async (server, env) =>
    {
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);
        result = await TrackCommand.Run(env, _logger, new DnvmSubCommand.TrackArgs
        {
            Channel = new Channel.Preview(),
            SdkDir = "preview"
        });
        Assert.Equal(TrackCommand.Result.Success, result);

        var ltsVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[0].LatestSdk, SemVersionStyles.Strict);
        var previewVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[1].LatestSdk, SemVersionStyles.Strict);

        var console = (TestConsole)env.Console;
        var trimOutput = console.Output;
        var unResult = await UninstallCommand.Run(env, _logger, previewVersion);
        var actualOutput = console.Output[trimOutput.Length..];
        Assert.Equal(0, unResult);
        Assert.DoesNotContain("SdkDirName", actualOutput);
        Assert.DoesNotContain(ltsVersion.ToString(), actualOutput);
    });

    [Fact]
    public Task SharedFeatureBandKept() => RunWithServer(async (server, env) =>
    {
        // Two SDKs in the same feature band (42.42.100 and 42.42.101 -> band 42.42.100)
        var firstVersion = new SemVersion(42, 42, 100);
        var secondVersion = new SemVersion(42, 42, 101);
        server.RegisterReleaseVersion(firstVersion, "lts", "active");
        server.RegisterReleaseVersion(secondVersion, "lts", "active");

        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = firstVersion }));
        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = secondVersion }));

        var band = firstVersion.ToFeatureBand();
        Assert.Equal(band, secondVersion.ToFeatureBand());
        var bandDir = UPath.Root / "dn" / "sdk-manifests" / band;
        Assert.True(env.DnvmHomeFs.DirectoryExists(bandDir));

        // Uninstalling one SDK must keep the band manifests since the other still shares it
        Assert.Equal(0, await UninstallCommand.Run(env, _logger, firstVersion));
        Assert.True(env.DnvmHomeFs.DirectoryExists(bandDir));

        // Uninstalling the last SDK in the band removes the manifests
        Assert.Equal(0, await UninstallCommand.Run(env, _logger, secondVersion));
        Assert.False(env.DnvmHomeFs.DirectoryExists(bandDir));
    });

    [Fact]
    public Task Dotnet6PrereleaseSharesStableFeatureBand() => RunWithServer(async (server, env) =>
    {
        var stableVersion = new SemVersion(6, 0, 100);
        var previewVersion = SemVersion.Parse("6.0.100-rc.2.21505.57", SemVersionStyles.Strict);
        server.RegisterReleaseVersion(stableVersion, "lts", "active");
        server.RegisterReleaseVersion(previewVersion, "lts", "preview");

        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = stableVersion }));
        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = previewVersion }));

        var band = stableVersion.ToFeatureBand();
        Assert.Equal(band, previewVersion.ToFeatureBand());
        var bandDir = UPath.Root / "dn" / "sdk-manifests" / band;

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, stableVersion));
        Assert.True(env.DnvmHomeFs.DirectoryExists(bandDir));
    });

    [Fact]
    public Task LaggingWorkloadBandKept() => RunWithServer(async (server, env) =>
    {
        // A single SDK archive lays down manifests under its own band *and* under an older band
        // used by the in-box mobile workloads. Two SDKs in different feature bands still share
        // that older band, so uninstalling one must not delete it.
        const string laggingBand = "42.42.100-mobile";
        server.ExtraManifestBands.Add(laggingBand);

        var firstVersion = new SemVersion(42, 42, 100);
        var secondVersion = new SemVersion(42, 42, 200);
        server.RegisterReleaseVersion(firstVersion, "lts", "active");
        server.RegisterReleaseVersion(secondVersion, "lts", "active");

        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = firstVersion }));
        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = secondVersion }));

        Assert.NotEqual(firstVersion.ToFeatureBand(), secondVersion.ToFeatureBand());

        var laggingDir = UPath.Root / "dn" / "sdk-manifests" / laggingBand;
        var secondBandDir = UPath.Root / "dn" / "sdk-manifests" / secondVersion.ToFeatureBand();
        Assert.True(env.DnvmHomeFs.DirectoryExists(laggingDir));
        Assert.True(env.DnvmHomeFs.DirectoryExists(secondBandDir));

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, secondVersion));
        Assert.False(env.DnvmHomeFs.DirectoryExists(secondBandDir));
        Assert.True(env.DnvmHomeFs.DirectoryExists(laggingDir));

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, firstVersion));
        Assert.False(env.DnvmHomeFs.DirectoryExists(laggingDir));
    });

    [Fact]
    public Task ManifestBandsAreAttributedToTheirArchive() => RunWithServer(async (server, env) =>
    {
        const string firstArchiveOnlyBand = "42.42.100-mobile";
        var firstVersion = new SemVersion(42, 42, 100);
        var secondVersion = new SemVersion(42, 42, 200);
        server.RegisterReleaseVersion(firstVersion, "lts", "active");
        server.RegisterReleaseVersion(secondVersion, "lts", "active");

        server.ExtraManifestBands.Add(firstArchiveOnlyBand);
        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = firstVersion }));

        server.ExtraManifestBands.Clear();
        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = secondVersion }));

        var manifest = await Manifest.ReadManifestUnsafe(env);
        var firstSdk = Assert.Single(manifest.InstalledSdks, sdk => sdk.SdkVersion == firstVersion);
        var secondSdk = Assert.Single(manifest.InstalledSdks, sdk => sdk.SdkVersion == secondVersion);
        Assert.Contains(firstArchiveOnlyBand, firstSdk.SdkManifestBands);
        Assert.DoesNotContain(firstArchiveOnlyBand, secondSdk.SdkManifestBands);

        var firstArchiveOnlyDir = UPath.Root / "dn" / "sdk-manifests" / firstArchiveOnlyBand;
        Assert.True(env.DnvmHomeFs.DirectoryExists(firstArchiveOnlyDir));

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, firstVersion));
        Assert.False(env.DnvmHomeFs.DirectoryExists(firstArchiveOnlyDir));
        Assert.True(env.DnvmHomeFs.DirectoryExists(
            UPath.Root / "dn" / "sdk-manifests" / secondVersion.ToFeatureBand()));
    });

    [Fact]
    public Task UnknownLegacyManifestBandsProtectsDirectory() => RunWithServer(async (server, env) =>
    {
        // An SDK installed by an older dnvm has no recorded manifest bands. We can't know which
        // band directories it needs, so no sdk-manifests cleanup may happen in that directory.
        var legacyVersion = new SemVersion(42, 42, 100);
        var newVersion = new SemVersion(42, 42, 200);
        server.RegisterReleaseVersion(legacyVersion, "lts", "active");
        server.RegisterReleaseVersion(newVersion, "lts", "active");

        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = legacyVersion }));
        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = newVersion }));

        using (var @lock = await ManifestLock.Acquire(env))
        {
            var manifest = await @lock.ReadManifest(env);
            manifest = manifest with
            {
                InstalledSdks = manifest.InstalledSdks
                    .Select(sdk => sdk.SdkVersion == legacyVersion
                        ? sdk with { SdkManifestBands = EqArray<string>.Empty }
                        : sdk)
                    .ToEq()
            };
            await @lock.WriteManifest(env, manifest);
        }

        var newBandDir = UPath.Root / "dn" / "sdk-manifests" / newVersion.ToFeatureBand();
        Assert.Equal(0, await UninstallCommand.Run(env, _logger, newVersion));
        Assert.True(env.DnvmHomeFs.DirectoryExists(newBandDir));
    });

    [Fact]
    public Task WorkloadMetadataKeepsFeatureBand() => RunWithServer(async (server, env) =>
    {
        var version = new SemVersion(42, 42, 100);
        server.RegisterReleaseVersion(version, "lts", "active");

        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = version }));

        var band = version.ToFeatureBand();
        var bandDir = UPath.Root / "dn" / "sdk-manifests" / band;
        var workloadRecord = UPath.Root / "dn" / "metadata" / "workloads" / "InstalledPacks"
            / "v1" / "Some.Workload.Pack" / "1.0.0" / band;
        env.DnvmHomeFs.CreateDirectory(workloadRecord.GetDirectory());
        env.DnvmHomeFs.WriteAllText(workloadRecord, "");

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, version));
        Assert.True(env.DnvmHomeFs.DirectoryExists(bandDir));
    });

    [Fact]
    public Task WindowsDesktopComponentVersionUsed() => RunWithServer(async (server, env) =>
    {
        var sdkVersion = SemVersion.Parse("10.0.100-preview.1.12345.6", SemVersionStyles.Strict);
        var releaseVersion = SemVersion.Parse("10.0.0-preview.1", SemVersionStyles.Strict);
        var runtimeVersion = SemVersion.Parse("10.0.0-preview.1.12345.7", SemVersionStyles.Strict);
        var windowsDesktopVersion = SemVersion.Parse("10.0.0-preview.1.12345.8", SemVersionStyles.Strict);
        var release = server.RegisterReleaseVersion(sdkVersion, "sts", "preview");
        release = release with
        {
            ReleaseVersion = releaseVersion,
            Runtime = release.Runtime with { Version = runtimeVersion },
            WindowsDesktop = release.WindowsDesktop with { Version = windowsDesktopVersion },
        };
        server.ChannelIndexMap[sdkVersion.ToMajorMinor()] = new() { Releases = [release] };

        Assert.Equal(InstallCommand.Result.Success,
            await InstallCommand.Run(env, _logger, new DnvmSubCommand.InstallArgs { SdkVersion = sdkVersion }));

        var installed = Assert.Single((await Manifest.ReadManifestUnsafe(env)).InstalledSdks);
        Assert.Equal(windowsDesktopVersion, installed.WindowsDesktopVersion);
        var sharedDir = UPath.Root / "dn" / "shared" / "Microsoft.WindowsDesktop.App" / windowsDesktopVersion.ToString();
        var refPackDir = UPath.Root / "dn" / "packs" / "Microsoft.WindowsDesktop.App.Ref" / windowsDesktopVersion.ToString();
        Assert.True(env.DnvmHomeFs.DirectoryExists(sharedDir));
        Assert.True(env.DnvmHomeFs.DirectoryExists(refPackDir));

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, sdkVersion));
        Assert.False(env.DnvmHomeFs.DirectoryExists(sharedDir));
        Assert.False(env.DnvmHomeFs.DirectoryExists(refPackDir));
    });

    [Fact]
    public Task UnknownLegacyWindowsDesktopVersionKept() => RunWithServer(async (server, env) =>
    {
        var sdkVersion = new SemVersion(10, 0, 100);
        var runtimeVersion = new SemVersion(10, 0, 0);
        var windowsDesktopVersion = SemVersion.Parse("10.0.0-preview.1.12345.8", SemVersionStyles.Strict);
        var manifest = Manifest.Empty.AddSdk(new InstalledSdk
        {
            SdkVersion = sdkVersion,
            ReleaseVersion = runtimeVersion,
            RuntimeVersion = runtimeVersion,
            AspNetVersion = runtimeVersion,
            WindowsDesktopVersion = null,
        });
        await Manifest.WriteManifestUnsafe(env, manifest);

        var sharedDir = UPath.Root / "dn" / "shared" / "Microsoft.WindowsDesktop.App" / windowsDesktopVersion.ToString();
        var refPackDir = UPath.Root / "dn" / "packs" / "Microsoft.WindowsDesktop.App.Ref" / windowsDesktopVersion.ToString();
        env.DnvmHomeFs.CreateDirectory(sharedDir);
        env.DnvmHomeFs.CreateDirectory(refPackDir);

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, sdkVersion));
        Assert.True(env.DnvmHomeFs.DirectoryExists(sharedDir));
        Assert.True(env.DnvmHomeFs.DirectoryExists(refPackDir));
    });

    [Fact]
    public Task UnknownLegacyWindowsDesktopVersionProtectsDirectory() => RunWithServer(async (server, env) =>
    {
        var legacySdkVersion = new SemVersion(9, 0, 100);
        var knownSdkVersion = new SemVersion(10, 0, 100);
        var windowsDesktopVersion = new SemVersion(10, 0, 0);
        var manifest = Manifest.Empty
            .AddSdk(new InstalledSdk
            {
                SdkVersion = legacySdkVersion,
                ReleaseVersion = new SemVersion(9, 0, 0),
                RuntimeVersion = new SemVersion(9, 0, 0),
                AspNetVersion = new SemVersion(9, 0, 0),
                WindowsDesktopVersion = null,
            })
            .AddSdk(new InstalledSdk
            {
                SdkVersion = knownSdkVersion,
                ReleaseVersion = windowsDesktopVersion,
                RuntimeVersion = windowsDesktopVersion,
                AspNetVersion = windowsDesktopVersion,
                WindowsDesktopVersion = windowsDesktopVersion,
            });
        await Manifest.WriteManifestUnsafe(env, manifest);

        var sharedDir = UPath.Root / "dn" / "shared" / "Microsoft.WindowsDesktop.App" / windowsDesktopVersion.ToString();
        var refPackDir = UPath.Root / "dn" / "packs" / "Microsoft.WindowsDesktop.App.Ref" / windowsDesktopVersion.ToString();
        env.DnvmHomeFs.CreateDirectory(sharedDir);
        env.DnvmHomeFs.CreateDirectory(refPackDir);

        Assert.Equal(0, await UninstallCommand.Run(env, _logger, knownSdkVersion));
        Assert.True(env.DnvmHomeFs.DirectoryExists(sharedDir));
        Assert.True(env.DnvmHomeFs.DirectoryExists(refPackDir));
    });

    [Fact]
    public Task MissingDirectoriesHandled() => RunWithServer(async (server, env) =>
    {
        // Install an SDK first
        var result = await TrackCommand.Run(env, _logger, new TrackCommand.Options
        {
            Channel = new Channel.Latest(),
        });
        Assert.Equal(TrackCommand.Result.Success, result);

        var ltsVersion = SemVersion.Parse(server.ReleasesIndexJson.ChannelIndices[0].LatestSdk, SemVersionStyles.Strict);
        
        // Manually remove some directories to simulate missing directories
        var sdkDir = UPath.Root / "dn" / "sdk" / ltsVersion.ToString();
        var runtimeDir = UPath.Root / "dn" / "shared" / "Microsoft.NETCore.App" / ltsVersion.ToString();
        env.DnvmHomeFs.DeleteDirectory(sdkDir, isRecursive: true);
        env.DnvmHomeFs.DeleteDirectory(runtimeDir, isRecursive: true);

        var console = (TestConsole)env.Console;
        var trimOutput = console.Output;
        
        // Uninstall should succeed despite missing directories
        var unResult = await UninstallCommand.Run(env, _logger, ltsVersion);
        var actualOutput = console.Output[trimOutput.Length..];
        
        Assert.Equal(0, unResult);
        Assert.Contains("not found, skipping", actualOutput);
        
        // Verify manifest is still updated correctly
        var manifest = await Manifest.ReadManifestUnsafe(env);
        Assert.Empty(manifest.InstalledSdks);
    });
}