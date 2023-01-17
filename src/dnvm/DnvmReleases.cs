
using System.Collections.Generic;
using Serde;
using static Dnvm.DnvmReleases;

namespace Dnvm;

[GenerateSerde]
public partial record struct DnvmReleases(Release LatestVersion)
{
    [GenerateSerde]
    public partial record struct Release(
        string Version,
        Dictionary<string, string> Artifacts);
}
