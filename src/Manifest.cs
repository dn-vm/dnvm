
using System.Collections.Immutable;
using Serde;

namespace Dnvm;

/// <summary>
/// List of installed workloads.
/// </summary>
[GenerateSerde]
[SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
internal sealed partial record Manifest
{
    public ImmutableArray<Workload> Workloads { get; init; } = ImmutableArray<Workload>.Empty;
}

[GenerateSerde]
[SerdeTypeOptions(MemberFormat = MemberFormat.CamelCase)]
internal partial record struct Workload
{
    public string Version { get; init; }
}