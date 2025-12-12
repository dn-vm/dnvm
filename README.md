# dnvm

Dnvm is a command-line program for installing and updating `dotnet` SDKs, targeted at Unix-like machines.

## Getting started

dnvm installs .NET SDKs to the dnvm home path, which is `~/.local/dnvm` on Linux, ` ~/Library/Application\ Support/dnvm/` on Mac, and `%LOCALAPPDATA%/dnvm` on Windows. Dnvm lets you:
- Install the latest release from channels through the `track` command
- Update SDK versions through the `update` command
- Cleanup old SDKs through the `prune` command (see [detailed documentation](docs/prune.md))
- Install specific SDKs through the `install` command.

The `--help` command can help you find more information on all available commands.

## Documentation

See [docs](docs) for more info on all commands.

## Channels

The simplest way to use dnvm is to track a channel. Channels are of two types: named channels and versions. Named channels are things like `latest`, `lts`, and `sts`. These correspond to the support status of various SDKs. For example, `lts` always corresponds to the currently supported .NET LTS SDK, while `latest` means the newest non-preview SDK in current support, LTS or STS.

## Help
```
$ dnvm -h
usage: dnvm [--enable-dnvm-previews] [-h | --help] <command>

Install and manage .NET SDKs.

Options:
    --enable-dnvm-previews  Enable dnvm previews.
    -h, --help  Show help information.

Commands:
    install  Install an SDK.
    track  Start tracking a new channel.
    selfinstall  Install dnvm to the local machine.
    update  Update the installed SDKs or dnvm itself.
    list  List installed SDKs.
    select  Select the active SDK directory.
    untrack  Remove a channel from the list of tracked channels.
    uninstall  Uninstall an SDK.
    prune  Remove all SDKs with older patch versions.
    restore  Restore the SDK listed in the global.json file.
```
