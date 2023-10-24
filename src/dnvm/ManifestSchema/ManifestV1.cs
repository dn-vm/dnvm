
using System.Collections.Immutable;
using Internal.CommandLine;
using Serde;

namespace Dnvm;

[GenerateDeserialize]
internal sealed partial record ManifestV1
{
    public ImmutableArray<Workload> Workloads { get; init; } = ImmutableArray<Workload>.Empty;

    [GenerateSerde]
    internal partial record struct Workload
    {
        public string Version { get; init; }
    }
}