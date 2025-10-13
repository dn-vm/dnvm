# Prune Command

The `dnvm prune` command removes older SDK versions from tracked channels to help manage disk space.

## Usage

```bash
dnvm prune
```

## How It Works

The prune command only removes SDKs that were installed through tracked channels. It will never remove:
- SDKs installed manually using `dnvm install <version>`
- SDKs restored from `global.json` files using `dnvm restore`
- SDKs from untracked channels

Within each tracked channel, SDKs are grouped by major.minor version (e.g., 8.0.x, 8.1.x). Only the newest patch version in each group is kept.

## Examples

```bash
# Track the 8.0 channel and update multiple times
dnvm track 8.0
dnvm update  # Installs 8.0.100
dnvm update  # Later installs 8.0.101
dnvm update  # Later installs 8.0.102

# Before prune: 8.0.100, 8.0.101, 8.0.102 (all from 8.0 channel)
dnvm prune
# After prune: 8.0.102 (only the latest remains)
```

```bash
# Mixed installation sources
dnvm track 8.0
dnvm update           # Installs 8.0.100 (tracked channel)
dnvm install 8.0.101  # Manual install
dnvm update           # Installs 8.0.102 (tracked channel)

dnvm prune
# After prune: 8.0.101 (manual), 8.0.102 (latest from channel)
# Note: 8.0.100 removed, 8.0.101 preserved (manual install)
```

## Related Commands

- [`dnvm list`](../README.md) - Show installed SDKs and their sources
- [`dnvm update`](update.md) - Update tracked channels to latest versions
- [`dnvm uninstall`](uninstall.md) - Remove a specific SDK version
