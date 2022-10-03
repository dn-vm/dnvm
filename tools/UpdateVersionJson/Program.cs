
// Usage: UpdateVersionJson <version>

using System.Runtime.CompilerServices;
using Dnvm;
using Serde.Json;

var version = GitVersionInformation.SemVer;
var rids = new[] { "linux-x64", "osx-x64", "win-x64" };
var artifacts = new Dictionary<string, string>();
foreach (var rid in rids)
{
    var zipSuffix = rid.StartsWith("win") ? "zip" : "tar.gz";
    artifacts[rid] = $"https://github.com/agocke/dnvm/releases/download/v{version}/dnvm-{version}-{rid}.{zipSuffix}";
}
var releases = new Update.Releases(new Update.Release(version, artifacts));

var serialized = JsonSerializer.Serialize(releases);
Console.WriteLine(serialized);
var writeDir = Path.Combine(ThisDir().Parent!.Parent!.FullName, "artifacts/gh-pages");
Directory.CreateDirectory(writeDir);
File.WriteAllText(Path.Combine(writeDir, "releases.json"), serialized);

static DirectoryInfo ThisDir([CallerFilePath]string path = "") => Directory.GetParent(path)!;
