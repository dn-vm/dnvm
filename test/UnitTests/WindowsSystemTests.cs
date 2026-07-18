using System.Security.Cryptography;
using Semver;
using Serde.Json;
using Xunit;

namespace Dnvm.Test;

public sealed class WindowsSystemTests
{
    [Theory]
    [InlineData(null, null, true, InstallScope.System)]
    [InlineData(null, "C:\\custom", true, InstallScope.User)]
    [InlineData("user", null, true, InstallScope.User)]
    [InlineData("system", null, true, InstallScope.System)]
    [InlineData(null, null, false, InstallScope.User)]
    public void ResolvesScope(
        string? scope,
        string? dnvmHome,
        bool isWindows,
        InstallScope expected)
    {
        Assert.Equal(expected, DnvmEnv.ResolveScope(scope, dnvmHome, isWindows));
    }

    [Fact]
    public void RejectsSystemScopeOutsideWindows()
    {
        Assert.Throws<ArgumentException>(() =>
            DnvmEnv.ResolveScope("system", null, isWindows: false));
    }

    [Fact]
    public void SystemHomeEnvironmentValueRetainsSystemScope()
    {
        Assert.Equal(
            InstallScope.System,
            DnvmEnv.ResolveScope(
                null,
                DnvmEnv.DefaultSystemDnvmHome,
                isWindows: true));
    }

    [Fact]
    public void PolicyRoundTripsWithoutInstalledInventory()
    {
        var version = SemVersion.Parse("10.0.100", SemVersionStyles.Strict);
        var policy = new WindowsPolicy()
            .Track(new Channel.Lts())
            .RecordInstallation(new Channel.Lts(), version);

        var json = JsonSerializer.Serialize(policy);
        var roundTrip = JsonSerializer.Deserialize<WindowsPolicy>(json);

        var tracked = Assert.Single(roundTrip.TrackedChannels());
        Assert.Equal(new Channel.Lts(), tracked.ChannelName);
        Assert.Equal(version, Assert.Single(tracked.InstalledSdkVersions));
        Assert.DoesNotContain("installedSdks", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PruneKeepsVersionsNeededByAnyTrackedChannel()
    {
        var oldVersion = SemVersion.Parse("10.0.100", SemVersionStyles.Strict);
        var newVersion = SemVersion.Parse("10.0.101", SemVersionStyles.Strict);
        var policy = new WindowsPolicy()
            .Track(new Channel.Lts())
            .RecordInstallation(new Channel.Lts(), oldVersion)
            .RecordInstallation(new Channel.Lts(), newVersion)
            .Track(new Channel.Latest())
            .RecordInstallation(new Channel.Latest(), oldVersion);

        Assert.Empty(WindowsSystemCommands.GetPruneCandidates(policy));
    }

    [Fact]
    public void PruneReturnsSupersededVersions()
    {
        var oldVersion = SemVersion.Parse("10.0.100", SemVersionStyles.Strict);
        var newVersion = SemVersion.Parse("10.0.101", SemVersionStyles.Strict);
        var policy = new WindowsPolicy()
            .Track(new Channel.Lts())
            .RecordInstallation(new Channel.Lts(), oldVersion)
            .RecordInstallation(new Channel.Lts(), newVersion);

        Assert.Equal(oldVersion, Assert.Single(WindowsSystemCommands.GetPruneCandidates(policy)));
    }

    [Fact]
    public void VerifiesPublishedInstallerHash()
    {
        using var directory = TestUtils.CreateTempDirectory();
        var path = Path.Combine(directory.Path, "installer.exe");
        File.WriteAllText(path, "test installer");
        var hash = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path)));

        Assert.True(WindowsSystemInstallBackend.VerifySha512(path, hash));
        Assert.False(WindowsSystemInstallBackend.VerifySha512(path, new string('0', 128)));
    }

    [Fact]
    public void SplitsRegisteredUninstallCommandWithoutShell()
    {
        var installerPath = Path.Combine(Path.GetTempPath(), "dotnet installer.exe");
        Assert.True(WindowsSystemInstallBackend.TrySplitCommandLine(
            $"\"{installerPath}\" /uninstall",
            out var executable,
            out var arguments));
        Assert.Equal(installerPath, executable);
        Assert.Equal("/uninstall", arguments);
    }
}
