# Prune Command

The `dnvm prune` command removes older SDK versions to help manage disk space and keep your .NET SDK installation clean. This document explains how the prune command works and what SDKs it will remove.

## Overview

The prune command only removes SDKs that were installed through **tracked channels**. It will never remove:
- SDKs installed manually using `dnvm install <version>`
- SDKs restored from `global.json` files using `dnvm restore`
- SDKs from untracked channels

## How Prune Works

### Channel-Based Pruning

The prune command operates on a per-channel basis:

1. **Examines tracked channels only** - Only channels that are currently being tracked by dnvm are considered
2. **Groups by major.minor version** - Within each channel, SDKs are grouped by their major.minor version (e.g., 8.0.x, 8.1.x, 9.0.x)
3. **Keeps the latest in each group** - For each major.minor group within a channel, only the newest patch version is kept
4. **Channel isolation** - SDKs from different channels never affect each other's pruning decisions

### What Gets Removed

SDKs are marked for removal when:
- They were installed through a tracked channel (`dnvm track` + `dnvm update`)
- There's a newer patch version in the same major.minor series within the same channel
- The physical SDK directory exists on disk

### What Gets Preserved

SDKs are always preserved when:
- They were installed manually (`dnvm install <specific-version>`)
- They were restored from global.json (`dnvm restore`)
- They're from an untracked channel
- They're the newest version in their major.minor group within their channel
- They're the only version in their major.minor group within their channel

## Examples

### Example 1: Basic Channel Pruning

```bash
# Track the 8.0 channel and update multiple times
dnvm track 8.0
dnvm update  # Installs 8.0.100
# Later, new patch releases come out
dnvm update  # Installs 8.0.101
dnvm update  # Installs 8.0.102

# Before prune: 8.0.100, 8.0.101, 8.0.102 (all from 8.0 channel)
dnvm prune
# After prune: 8.0.102 (only the latest from the 8.0 channel remains)
```

### Example 2: Mixed Installation Sources

```bash
# Install SDKs from different sources
dnvm track 8.0
dnvm update           # Installs 8.0.100 (tracked channel)
dnvm install 8.0.101  # Manual install
dnvm update           # Installs 8.0.102 (tracked channel)

# Before prune: 8.0.100 (channel), 8.0.101 (manual), 8.0.102 (channel)
dnvm prune
# After prune: 8.0.101 (manual), 8.0.102 (channel)
# Note: 8.0.100 removed (older in channel), 8.0.101 preserved (manual install)
```

### Example 3: Multiple Channels

```bash
# Track different channels
dnvm track 8.0        # Latest stable 8.0
dnvm track preview    # Preview releases
dnvm update

# You might have:
# - 8.0.100, 8.0.102 from 8.0 channel
# - 9.0.100-preview.1, 9.0.100-preview.2 from preview channel

dnvm prune
# After prune:
# - 8.0.102 (latest from 8.0 channel)
# - 9.0.100-preview.2 (latest from preview channel)
```

### Example 4: Multiple Major.Minor Versions

```bash
# Track a channel that spans multiple major.minor versions
dnvm track latest
# Over time, this might install:
# - 8.0.100, 8.0.102 (from when 8.0 was latest)
# - 8.1.100 (when 8.1 became latest)
# - 9.0.100, 9.0.101 (when 9.0 became latest)

dnvm prune
# After prune: 8.0.102, 8.1.100, 9.0.101
# (Latest patch version from each major.minor group)
```

## Command Usage

```bash
# Remove older SDK versions from tracked channels
dnvm prune

# Get help on the prune command
dnvm prune --help
```

## Safety Features

- **Dry-run reporting**: The command reports what it will remove before actually removing anything
- **Missing directory handling**: If an SDK is tracked in the manifest but its directory is missing, the command will note this and update the manifest accordingly
- **Conservative approach**: When in doubt, the command preserves SDKs rather than removing them

## Technical Details

### Manifest Tracking

dnvm maintains a manifest file that tracks:
- Which SDKs are installed
- Which channels are tracked
- Which SDK versions were installed through which channels

The prune command uses this information to determine which SDKs are eligible for removal.

### Version Comparison

When comparing SDK versions within the same major.minor group, dnvm uses semantic version ordering that properly handles:
- Release versions (e.g., 8.0.100)
- Preview versions (e.g., 8.0.100-preview.1)
- Release candidate versions (e.g., 8.0.100-rc.1)

## Troubleshooting

### "SDK not found, skipping" Messages

If you see messages about SDKs not being found during pruning, this means:
- The SDK is tracked in the manifest but its directory doesn't exist on disk
- This could happen if you manually deleted SDK directories
- The prune command will update the manifest to remove these stale entries

### Unexpected SDK Preservation

If an SDK you expected to be removed is still present:
- Check if it was installed manually (`dnvm list` shows installation source)
- Verify the channel is still tracked (`dnvm list` shows tracked channels)
- Check if it's the only or newest version in its major.minor group

## Related Commands

- `dnvm list` - Show installed SDKs and their sources
- `dnvm track` - Start tracking a channel
- `dnvm untrack` - Stop tracking a channel
- `dnvm update` - Update tracked channels to latest versions
- `dnvm install` - Manually install a specific SDK version
