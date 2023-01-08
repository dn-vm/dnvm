
using System.Collections.Generic;
using Serde;

namespace Dnvm;

[GenerateSerde]
internal partial record struct Releases(Release LatestVersion);

[GenerateSerde]
internal partial record struct Release(
    string Version,
    Dictionary<string, string> Artifacts);
