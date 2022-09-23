
using Serde;

namespace Dnvm;

[GenerateSerde]
[SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
internal partial record struct Workload(string Version, string Path)
{
	public Workload() : this("", "") { }

	public Workload(string Version)
		: this(Version,
			System.IO.Path.Combine(
				Utilities.LocalInstallLocation,
				Utilities.EscapeFilename(Version)))
	{ }
}