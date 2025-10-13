# Update Command

The `dnvm update` command checks for and installs newer SDK versions for all tracked channels.

## Usage

```bash
dnvm update [--yes]
```

- `--yes` - Optional. Skip confirmation prompts and install updates automatically

## How It Works

The update command checks each tracked channel for available updates:

1. For channels with installed SDKs, it compares the newest installed version to the latest available version
2. For tracked channels with no installed versions, it installs the latest available version
3. If a newer version is available, it prompts to install (or installs automatically with `--yes`)

## Examples

```bash
# Check for updates and prompt before installing
dnvm update

# Install updates without prompting
dnvm update --yes
```

## Related Commands

- [`dnvm track`](../README.md) - Track a release channel for automatic updates
- [`dnvm untrack`](../README.md) - Stop tracking a release channel
- [`dnvm list`](../README.md) - See all installed and tracked SDK versions
