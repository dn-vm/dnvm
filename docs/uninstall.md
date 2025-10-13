# Uninstall Command

The `dnvm uninstall` command removes a specific .NET SDK version from your system, including its associated runtime components that aren't needed by other SDKs.

## Usage

```bash
dnvm uninstall <version> [--dir <directory>]
```

- `<version>` - The exact SDK version to uninstall (e.g., `8.0.100`, `9.0.0-preview.1`)
- `--dir <directory>` - Optional. Specify which SDK directory to uninstall from

## How It Works

The uninstall command removes the SDK and checks which runtime components (runtimes, ASP.NET, templates, etc.) are still needed by other installed SDKs. Only components that are no longer needed are removed.

## Examples

```bash
# Uninstall .NET 8.0.100 SDK
dnvm uninstall 8.0.100

# Uninstall from a specific SDK directory
dnvm uninstall 8.0.100 --dir /custom/dotnet
```

## Related Commands

- [`dnvm list`](../README.md) - See all installed SDK versions
- [`dnvm prune`](prune.md) - Automatically remove older versions from tracked channels
- [`dnvm install`](../README.md) - Install a specific SDK version