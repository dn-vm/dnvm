
using System.Collections.Generic;
using Serde;

namespace Dnvm;

[GenerateSerde]
public partial record DnvmReleases(DnvmReleases.Release LatestVersion)
{
    public Release? LatestPreview { get; init; }

    [GenerateSerde]
    public partial record Release(
        string Version,
        Dictionary<string, string> Artifacts);
}