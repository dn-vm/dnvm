# dnvm

dnvm is the "dotnet version manager." This is a rethinking of the original dnvm project. This dnvm is a command-line interface for installing and updating different dotnet SDKs. It currently only supports installing user-local SDKs, as opposed to machine-wide.

```
dnvm 0.5.3 28de6c1feb5c9b660c1df4a98231a0287928b762

usage: dnvm <command> [<args>]

    track          Start tracking a new channel
    selfinstall    Install dnvm to the local machine
    update         Update the installed SDKs or dnvm itself
    list           List installed SDKs
    select         Select the active SDK directory
    untrack        Remove a channel from the list of tracked channels
    uninstall      Uninstall an SDK
    prune          Remove all SDKs with older patch versions.
```
