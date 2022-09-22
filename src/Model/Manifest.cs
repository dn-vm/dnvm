
using Serde;
using Serde.Json;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Dnvm;

internal static class ManifestHelpers
{
	public static string DefaultManifestPath = Path.Combine(Utilities.LocalInstallLocation, "dnvmManifest.json");

	public static Manifest Instance { get; set; } = TryGetManifest(out var manifest, DefaultManifestPath) ? manifest : new Manifest();

	public static bool TryGetManifest([NotNullWhen(true)] out Manifest? manifest, string manifestPath)
	{
		try
		{
			manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
			return true;
		}
		catch
		{
			manifest = null;
			return false;
		}
	}
}
/// <summary>
/// List of installed workloads.
/// </summary>
[GenerateSerde]
[SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
internal sealed partial record Manifest(ImmutableArray<Workload> Workloads, Workload? Active, [property: SerdeMemberOptions(SerializeNull = false)] string? ManifestPath = null)
{
	public Manifest() : this(ImmutableArray<Workload>.Empty, null) { }
	public void WriteOut() => File.WriteAllText(ManifestPath ?? ManifestHelpers.DefaultManifestPath, JsonSerializer.Serialize(this));
}
