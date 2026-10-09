// Canonical compile-checked mirror of the docs/*.md guides (#45).
// Each topic file below corresponds to one guide page. Keep the representative
// API tokens in sync with the guides: verify-docs-samples.sh fails when a
// documented sample drifts from its fixture. This project consumes the packed
// release-candidate packages (no ProjectReference fallback when
// SparseFragmentsPackageVersion is set), so CI fails when the public
// generated API breaks a documented sample.

KeyedCollectionsSamples.Run();
RebaseSamples.Run();
ChangePayloadDocsSamples.Run();
MergeStrategiesSamples.Run();
CloningAndOwnershipSamples.Run();
ModelShapesSamples.Run();
UiFrameworksSamples.Run();
VerifiedSamples.Run();

Console.WriteLine("SparseFragments docs consumer passed.");

internal static class DocsCheck
{
    public static void Require(bool condition, string capability)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Failed: " + capability);
        }
    }
}
