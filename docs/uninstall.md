# Uninstall Command

The `dnvm uninstall` command removes a specific .NET SDK version from your system, including its associated runtime components that aren't needed by other SDKs.

## Usage

```bash
dnvm uninstall <version> [--sdk-dir <directory>] [-y]
```

- `<version>` - The exact SDK version to uninstall (e.g., `8.0.100`, `9.0.0-preview.1`)
- `--sdk-dir <directory>` - In user scope, specify which SDK directory to uninstall from
- `-y` - Confirm removal without prompting in Windows system scope

## How It Works

In user scope, the uninstall command removes the SDK and checks which runtime components (runtimes, ASP.NET, templates, etc.) are still needed by other installed SDKs.

In Windows system scope, dnvm delegates removal to the SDK's registered Windows uninstaller. It never deletes files directly and refuses to remove Visual Studio-managed SDKs or SDKs that Windows does not report as independently removable.

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