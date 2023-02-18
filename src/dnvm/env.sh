#!/bin/sh
# Prepend dnvm and SDK dirs to the path, unless already there
case ":${PATH}:" in
    *:"{install_loc}":*)
        ;;
    *)
        export PATH="{install_loc}:$PATH"
        ;;
esac
export DOTNET_ROOT="{sdk_install_loc}"