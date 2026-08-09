# Uninstall Command

The `dnvm uninstall` command removes a specific .NET SDK version from your system, including its associated runtime components that aren't needed by other SDKs.

## Usage

```bash
dnvm uninstall <version> [--dir <directory>]
```

- `<version>` - The exact SDK version to uninstall (e.g., `8.0.100`, `9.0.0-preview.1`)
- `--dir <directory>` - Optional. Specify which SDK directory to uninstall from

## How It Works

The uninstall command removes the SDK and checks which shared components are still needed by other installed SDKs. Only components that are no longer needed are removed. This includes:

- Shared frameworks (`Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, `Microsoft.WindowsDesktop.App`)
- Reference and host packs under `packs/` (`Microsoft.NETCore.App.Ref`, `Microsoft.AspNetCore.App.Ref`, `Microsoft.NETCore.App.Host.<rid>`, `Microsoft.WindowsDesktop.App.Ref`)
- Host `fxr`, templates, and the SDK itself
- Workload manifests under `sdk-manifests/<feature-band>` (removed only when no other installed SDK contributed that directory and no workload installation metadata references it). A single SDK archive can lay down manifests under several feature bands, so dnvm records the set of directories each install contributed. SDKs installed by dnvm versions older than this feature have no recorded set, and their presence disables `sdk-manifests` cleanup for that SDK directory entirely.

Workload packs installed via `dotnet workload install` (for example the iOS, Android, and MacCatalyst packs) are not tracked by dnvm and are not removed. Use `dotnet workload clean` to reclaim that space.

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