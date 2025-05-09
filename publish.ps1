pushd $PSScriptRoot

# Extract version from Directory.Build.props
[xml]$props = Get-Content "$PSScriptRoot/Directory.Build.props"
$version = $props.Project.PropertyGroup.SemVer

$rid='win-x64'

dotnet publish --sc -r $rid -c Release src/dnvm/dnvm.csproj
Compress-Archive -Force "artifacts/publish/dnvm/release_$rid/dnvm.exe" "artifacts/dnvm-$version-$rid.zip"

popd
