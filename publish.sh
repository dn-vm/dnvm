#!/bin/bash

set -e
set -u

usage() { echo "Usage: $0 [-r RID]" 1>&2; exit 1; }

rid=
while getopts ":r:" o; do
    case "${o}" in
        r)
            rid=${OPTARG}
            ;;
        *)
            usage
            ;;
    esac
done
shift $((OPTIND-1))

if [[ $# -gt 0 ]]; then
    usage
fi

cd "$(dirname "$0")"

# Extract version from Directory.Build.props
version=$(grep "<SemVer>" Directory.Build.props | sed -E 's/.*<SemVer>([^<]+)<\/SemVer>.*/\1/')

if [[ -z "${rid}" ]]; then
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
fi

dotnet publish --sc -r $rid -c Release src/dnvm/dnvm.csproj
dotnet publish --sc -r $rid -c Release tools/mk-keys/mk-keys.csproj
tar -C ./artifacts/publish/dnvm/release_$rid/ -cvzf ./artifacts/dnvm-$version-$rid.tar.gz dnvm
