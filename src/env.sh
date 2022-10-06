#!/bin/sh
# Prepend dotnet dir to the path, unless it's already there
# steal rustup trick of matching with ':' on both sides
case ":${PATH}:" in
    *:"{install_loc}":*)
        ;;
    *)
        export PATH="{install_loc}:$PATH"
        ;;
esac
if [ -z "$DOTNET_ROOT" ]; then
    export DOTNET_ROOT="{install_loc}"
fi