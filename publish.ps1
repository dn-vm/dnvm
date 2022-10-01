
$version=$(dotnet gitversion /showvariable semver)
$rid='win-x64'

dotnet publish --sc -r $rid -c Release src/dnvm.csproj
Compress-Archive artifacts/bin/dnvm/Release/net7.0/$rid/publish/dnvm.exe artifacts/dnvm-$version-$rid.zip