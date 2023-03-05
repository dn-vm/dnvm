
pushd $PSScriptRoot

dotnet tool restore

$version=$(dotnet gitversion /showvariable semver)
$rid='win-x64'

dotnet publish --sc -r $rid -c Release src/dnvm/dnvm.csproj
Compress-Archive -Force "artifacts/bin/dnvm/Release/net8.0/$rid/publish/dnvm.exe" "artifacts/dnvm-$version-$rid.zip"

popd