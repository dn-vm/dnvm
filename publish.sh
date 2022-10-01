#!/bin/sh

set -e
set -u

version=`dotnet gitversion /showvariable semver`

# On osx we always use x64 for the moment as NativeAOT doesn't support osx-arm64
if [[ $(uname) == 'Darwin' ]]; then
    rid='osx-x64'
else
    rid='linux-x64'
fi

dotnet publish --sc -r $rid -c Release src/dnvm.csproj
tar -C artifacts/bin/dnvm/Release/net7.0/$rid/publish/ -cvzf artifacts/dnvm-$version-$rid.tar.gz dnvm