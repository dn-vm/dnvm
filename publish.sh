#!/bin/bash

set -e
set -u

cd "$(dirname "$0")"

dotnet tool restore

version=$(dotnet gitversion /showvariable semver)
if [[ $(uname) == 'Darwin' ]]; then
    osname='osx'
else
    osname='linux'
fi

name=$(uname -m)
if [[ $name == 'arm64' || $name == 'aarch64' ]]; then
    arch='arm64'
else
    arch='x64'
fi

rid=$osname-$arch

dotnet publish --sc -r $rid -c Release src/dnvm.csproj
tar -C ./artifacts/bin/dnvm/Release/net7.0/$rid/publish/ -cvzf ./artifacts/dnvm-$version-$rid.tar.gz dnvm
