using System.Collections.Immutable;

internal record struct Artifact(string RID, string ArtifactURI);

internal partial record struct Release (string ReleaseID, ImmutableArray<Artifact> Artifacts);