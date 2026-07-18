using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Semver;

namespace Dnvm;

public sealed record SystemSdkInstallation(
    SemVersion Version,
    string Architecture,
    string InstallLocation,
    string? ProductCode,
    string? UninstallCommand,
    bool IsUninstallable,
    bool IsVisualStudioManaged,
    string RegistrationSource);

public enum SystemInstallResult
{
    Success,
    RequiresElevation,
    InstallerUnavailable,
    DownloadFailed,
    IntegrityCheckFailed,
    InstallerFailed,
    RegistrationNotFound,
    UninstallRefused,
}

public interface ISystemInstallBackend
{
    string DotnetInstallLocation { get; }
    bool IsElevated { get; }
    IReadOnlyList<SystemSdkInstallation> GetInstalledSdks();
    Task<SystemInstallResult> Install(
        DnvmEnv env,
        Logger logger,
        ChannelReleaseIndex.Component sdk);
    Task<SystemInstallResult> Uninstall(
        DnvmEnv env,
        Logger logger,
        SystemSdkInstallation installation);
}

[SupportedOSPlatform("windows")]
public sealed partial class WindowsSystemInstallBackend : ISystemInstallBackend
{
    private const string DotnetSetupKey = @"SOFTWARE\dotnet\Setup\InstalledVersions";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const int ErrorSuccess = 0;
    private const int ErrorNoMoreItems = 259;

    public string DotnetInstallLocation { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "dotnet");

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public IReadOnlyList<SystemSdkInstallation> GetInstalledSdks()
    {
        var registrations = ReadUninstallRegistrations()
            .Concat(ReadWindowsInstallerProducts())
            .ToList();
        var result = new Dictionary<(SemVersion Version, string Architecture), SystemSdkInstallation>();

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var installedVersions = hklm.OpenSubKey(DotnetSetupKey);
            if (installedVersions is null)
            {
                continue;
            }

            foreach (var architecture in installedVersions.GetSubKeyNames())
            {
                using var architectureKey = installedVersions.OpenSubKey(architecture);
                using var sdkKey = architectureKey?.OpenSubKey("sdk");
                if (architectureKey is null || sdkKey is null)
                {
                    continue;
                }

                var installLocation = architectureKey.GetValue("InstallLocation") as string
                    ?? DotnetInstallLocation;
                foreach (var valueName in sdkKey.GetValueNames())
                {
                    if (!SemVersion.TryParse(valueName, SemVersionStyles.Strict, out var version))
                    {
                        continue;
                    }

                    var registration = registrations.FirstOrDefault(r =>
                        r.Version == version
                        && (r.Architecture.Length == 0
                            || string.Equals(r.Architecture, architecture, StringComparison.OrdinalIgnoreCase)));
                    result[(version, architecture)] = new SystemSdkInstallation(
                        version,
                        architecture,
                        installLocation,
                        registration?.ProductCode,
                        registration?.UninstallCommand,
                        registration?.IsUninstallable ?? false,
                        registration?.IsVisualStudioManaged ?? false,
                        registration?.Source ?? "dotnet setup registry");
                }
            }
        }

        return result.Values
            .OrderByDescending(x => x.Version, SemVersion.SortOrderComparer)
            .ThenBy(x => x.Architecture, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SystemInstallResult> Install(
        DnvmEnv env,
        Logger logger,
        ChannelReleaseIndex.Component sdk)
    {
        if (!IsElevated)
        {
            return SystemInstallResult.RequiresElevation;
        }

        var rid = Utilities.CurrentRID.ToString();
        var installer = sdk.Files.FirstOrDefault(f =>
            string.Equals(f.Rid, rid, StringComparison.OrdinalIgnoreCase)
            && f.Url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (installer is null
            || !Uri.TryCreate(installer.Url, UriKind.Absolute, out var installerUri)
            || installerUri.Scheme != Uri.UriSchemeHttps)
        {
            return SystemInstallResult.InstallerUnavailable;
        }

        using var tempDir = new DirectoryResource(Directory.CreateTempSubdirectory().FullName);
        var installerPath = Path.Combine(tempDir.Path, Path.GetFileName(installerUri.LocalPath));
        var downloadError = await SpectreUtil.DownloadWithProgress(
            env.HttpClient,
            env.Console,
            logger,
            installerPath,
            installerUri.ToString(),
            "Downloading official .NET SDK installer");
        if (downloadError is not null)
        {
            env.Console.Error(downloadError);
            return SystemInstallResult.DownloadFailed;
        }

        if (installer.Hash is not { Length: > 0 })
        {
            env.Console.Error("The Microsoft release metadata does not provide a SHA-512 hash for this installer.");
            return SystemInstallResult.IntegrityCheckFailed;
        }
        if (!VerifySha512(installerPath, installer.Hash))
        {
            env.Console.Error("The downloaded .NET SDK installer did not match the published SHA-512 hash.");
            return SystemInstallResult.IntegrityCheckFailed;
        }

        var processResult = await ProcUtil.RunWithOutput(
            installerPath,
            "/install /quiet /norestart");
        logger.Log(processResult.Out);
        logger.Log(processResult.Error);
        if (processResult.ExitCode is not (0 or 1641 or 3010))
        {
            env.Console.Error($"The .NET SDK installer failed with exit code {processResult.ExitCode}.");
            return SystemInstallResult.InstallerFailed;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (GetInstalledSdks().Any(x => x.Version == sdk.Version))
            {
                return SystemInstallResult.Success;
            }
            await Task.Delay(200);
        }

        return SystemInstallResult.RegistrationNotFound;
    }

    public async Task<SystemInstallResult> Uninstall(
        DnvmEnv env,
        Logger logger,
        SystemSdkInstallation installation)
    {
        if (!IsElevated)
        {
            return SystemInstallResult.RequiresElevation;
        }
        if (!installation.IsUninstallable
            || installation.IsVisualStudioManaged
            || installation.UninstallCommand is null)
        {
            return SystemInstallResult.UninstallRefused;
        }
        if (!TrySplitCommandLine(installation.UninstallCommand, out var executable, out var arguments))
        {
            return SystemInstallResult.UninstallRefused;
        }

        var result = await ProcUtil.RunWithOutput(executable, EnsureQuietUninstall(arguments));
        logger.Log(result.Out);
        logger.Log(result.Error);
        if (result.ExitCode is not (0 or 1605 or 1641 or 3010))
        {
            env.Console.Error($"The registered .NET SDK uninstaller failed with exit code {result.ExitCode}.");
            return SystemInstallResult.InstallerFailed;
        }
        return SystemInstallResult.Success;
    }

    internal static bool VerifySha512(string path, string expectedHash)
    {
        try
        {
            var expected = Convert.FromHexString(expectedHash.Trim());
            using var stream = File.OpenRead(path);
            var actual = SHA512.HashData(stream);
            return expected.Length == actual.Length
                && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static bool TrySplitCommandLine(
        string commandLine,
        out string executable,
        out string arguments)
    {
        executable = "";
        arguments = "";
        var value = commandLine.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] == '"')
        {
            var closingQuote = value.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                return false;
            }
            executable = value[1..closingQuote];
            arguments = value[(closingQuote + 1)..].TrimStart();
            return Path.IsPathFullyQualified(executable);
        }

        var exeEnd = -1;
        var searchStart = 0;
        while (searchStart < value.Length)
        {
            var match = value.IndexOf(".exe", searchStart, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                break;
            }
            var afterExtension = match + 4;
            if (afterExtension == value.Length || char.IsWhiteSpace(value[afterExtension]))
            {
                exeEnd = afterExtension;
                break;
            }
            searchStart = afterExtension;
        }
        if (exeEnd < 0)
        {
            return false;
        }
        executable = value[..exeEnd].Trim();
        arguments = value[exeEnd..].TrimStart();
        return Path.IsPathFullyQualified(executable)
            || string.Equals(executable, "msiexec.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureQuietUninstall(string arguments)
    {
        if (arguments.Contains("/quiet", StringComparison.OrdinalIgnoreCase)
            || arguments.Contains("/qn", StringComparison.OrdinalIgnoreCase))
        {
            return arguments;
        }
        return arguments + " /quiet /norestart";
    }

    private static IEnumerable<InstallerRegistration> ReadUninstallRegistrations()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = hklm.OpenSubKey(UninstallKey);
            if (uninstall is null)
            {
                continue;
            }

            foreach (var keyName in uninstall.GetSubKeyNames())
            {
                using var product = uninstall.OpenSubKey(keyName);
                var displayName = product?.GetValue("DisplayName") as string;
                if (displayName is null
                    || !displayName.Contains(".NET SDK", StringComparison.OrdinalIgnoreCase)
                    || TryParseVersion(displayName) is not { } version)
                {
                    continue;
                }

                var parentDisplayName = product!.GetValue("ParentDisplayName") as string;
                var systemComponent = Convert.ToInt32(
                    product.GetValue("SystemComponent") ?? 0,
                    CultureInfo.InvariantCulture) != 0;
                var noRemove = Convert.ToInt32(
                    product.GetValue("NoRemove") ?? 0,
                    CultureInfo.InvariantCulture) != 0;
                var visualStudioManaged = systemComponent
                    || (parentDisplayName?.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) ?? false);
                var uninstallCommand = product.GetValue("QuietUninstallString") as string
                    ?? product.GetValue("UninstallString") as string;
                var architecture = ParseArchitecture(displayName);
                var productCode = Guid.TryParse(keyName, out _) ? keyName : null;

                yield return new InstallerRegistration(
                    version,
                    architecture,
                    productCode,
                    uninstallCommand,
                    !visualStudioManaged && !noRemove && uninstallCommand is not null,
                    visualStudioManaged,
                    "Windows uninstall registry");
            }
        }
    }

    private static IEnumerable<InstallerRegistration> ReadWindowsInstallerProducts()
    {
        var productCode = new StringBuilder(39);
        for (uint index = 0; ; index++)
        {
            productCode.Clear();
            productCode.EnsureCapacity(39);
            var result = MsiEnumProducts(index, productCode);
            if (result == ErrorNoMoreItems)
            {
                yield break;
            }
            if (result != ErrorSuccess)
            {
                continue;
            }

            var code = productCode.ToString();
            var productName = GetMsiProductInfo(code, "ProductName");
            if (productName is null
                || !productName.Contains(".NET SDK", StringComparison.OrdinalIgnoreCase)
                || TryParseVersion(productName) is not { } version)
            {
                continue;
            }

            yield return new InstallerRegistration(
                version,
                ParseArchitecture(productName),
                code,
                $"msiexec.exe /x {code}",
                true,
                false,
                "Windows Installer");
        }
    }

    private static string? GetMsiProductInfo(string productCode, string property)
    {
        var length = 0;
        var firstResult = MsiGetProductInfo(productCode, property, null, ref length);
        if (firstResult is not (ErrorSuccess or 234))
        {
            return null;
        }

        length++;
        var value = new StringBuilder(length);
        return MsiGetProductInfo(productCode, property, value, ref length) == ErrorSuccess
            ? value.ToString()
            : null;
    }

    private static SemVersion? TryParseVersion(string displayName)
    {
        var match = SdkVersionRegex().Match(displayName);
        return match.Success
            && SemVersion.TryParse(match.Groups[1].Value, SemVersionStyles.Strict, out var version)
            ? version
            : null;
    }

    private static string ParseArchitecture(string displayName)
    {
        foreach (var architecture in new[] { "arm64", "x64", "x86" })
        {
            if (displayName.Contains(architecture, StringComparison.OrdinalIgnoreCase))
            {
                return architecture;
            }
        }
        return "";
    }

    [GeneratedRegex(@"(?<!\d)(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)(?!\d)")]
    private static partial Regex SdkVersionRegex();

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiEnumProductsW")]
    private static extern int MsiEnumProducts(uint productIndex, StringBuilder productCode);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetProductInfoW")]
    private static extern int MsiGetProductInfo(
        string productCode,
        string property,
        StringBuilder? value,
        ref int valueLength);

    private sealed record InstallerRegistration(
        SemVersion Version,
        string Architecture,
        string? ProductCode,
        string? UninstallCommand,
        bool IsUninstallable,
        bool IsVisualStudioManaged,
        string Source);
}
