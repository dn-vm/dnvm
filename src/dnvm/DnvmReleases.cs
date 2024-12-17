
using System.Collections.Generic;
using Serde;
using static Dnvm.DnvmReleases;

namespace Dnvm;

[GenerateSerde]
public partial record DnvmReleases(Release LatestVersion)
{
    [GenerateSerde]
    public partial record Release(
        string Version,
        Dictionary<string, string> Artifacts);
}
