
pushd $PSScriptRoot

dotnet tool restore

$version=$(dotnet gitversion /showvariable semver)
$rid='win-x64'

dotnet publish --sc -r $rid -c Release src/dnvm/dnvm.csproj
Compress-Archive -Force "artifacts/publish/dnvm/release_$rid/dnvm.exe" "artifacts/dnvm-$version-$rid.zip"

popd
