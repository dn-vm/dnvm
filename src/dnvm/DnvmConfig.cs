using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Serde;
using Serde.Json;
using Zio;
using Zio.FileSystems;

namespace Dnvm;

/// <summary>
/// Represents the dnvm configuration file.
/// </summary>
[GenerateSerde]
public sealed partial record DnvmConfig
{
    public static readonly DnvmConfig Default = new();

    /// <summary>
    /// Whether to enable dnvm preview releases in the update command.
    /// </summary>
    public bool PreviewsEnabled { get; init; } = false;
}

/// <summary>
/// Static methods for config file operations.
/// </summary>
public static class DnvmConfigFile
{
    private const string ConfigFileName = "dnvmConfig.json";
    
    // Allow tests to override the config directory
    public static string? TestConfigDirectory { get; set; }

    /// <summary>
    /// Get the platform-specific config directory path.
    /// - Linux: ~/.config/dnvm/ (XDG_CONFIG_HOME/dnvm)
    /// - macOS: ~/Library/Application Support/dnvm/
    /// - Windows: %LOCALAPPDATA%/dnvm/
    /// </summary>
    private static string GetConfigDirectory()
    {
        if (TestConfigDirectory is not null)
        {
            return TestConfigDirectory;
        }

        // Allow tests to override config directory via environment variable
        var testOverride = Environment.GetEnvironmentVariable("DNVM_TEST_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(testOverride))
        {
            return testOverride;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Use XDG_CONFIG_HOME on Linux, defaulting to ~/.config
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var configBase = string.IsNullOrWhiteSpace(xdgConfigHome)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdgConfigHome;
            return Path.Combine(configBase, "dnvm");
        }
        else
        {
            // On macOS and Windows, use LocalApplicationData
            // This is ~/Library/Application Support on macOS and %LOCALAPPDATA% on Windows
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dnvm");
        }
    }

    private static IFileSystem GetConfigFileSystem()
    {
        var configDir = GetConfigDirectory();
        Directory.CreateDirectory(configDir);
        return new SubFileSystem(
            DnvmEnv.PhysicalFs,
            DnvmEnv.PhysicalFs.ConvertPathFromInternal(configDir));
    }

    private static UPath ConfigPath => UPath.Root / ConfigFileName;

    /// <summary>
    /// Reads the config file from the platform-specific config directory.
    /// Returns the default config if the file does not exist.
    /// </summary>
    public static DnvmConfig Read()
    {
        try
        {
            var fs = GetConfigFileSystem();
            if (!fs.FileExists(ConfigPath))
            {
                return DnvmConfig.Default;
            }

            var text = fs.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<DnvmConfig>(text);
        }
        catch (Exception)
        {
            // If there's any error reading or parsing the config, return default
            return DnvmConfig.Default;
        }
    }

    /// <summary>
    /// Writes the config file to the platform-specific config directory.
    /// </summary>
    public static void Write(DnvmConfig config)
    {
        var fs = GetConfigFileSystem();
        var tempFileName = $"{ConfigFileName}.{Path.GetRandomFileName()}.tmp";
        var tempPath = UPath.Root / tempFileName;

        var text = JsonSerializer.Serialize(config);

        // Write to temporary file first
        fs.WriteAllText(tempPath, text, Encoding.UTF8);

        // Create backup of existing config if it exists
        if (fs.FileExists(ConfigPath))
        {
            var backupPath = UPath.Root / $"{ConfigFileName}.backup";
            try
            {
                // Should not throw if the file doesn't exist
                fs.DeleteFile(backupPath);
                fs.MoveFile(ConfigPath, backupPath);
            }
            catch (IOException)
            {
                // Best effort cleanup - ignore if we can't delete the backup file
            }
        }

        // Atomic rename operation
        fs.MoveFile(tempPath, ConfigPath);

        // Clean up temporary file
        try
        {
            fs.DeleteFile(tempPath);
        }
        catch (IOException)
        {
            // Best effort cleanup - ignore if we can't delete the temp file
        }
    }
}
