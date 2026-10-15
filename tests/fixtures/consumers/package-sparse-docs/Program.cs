// Compile-checked consumers for docs guides; sample drift fails verification.
// The project uses packed packages to catch public API regressions.

KeyedCollectionsSamples.Run();
RebaseSamples.Run();
ChangePayloadDocsSamples.Run();
MergeStrategiesSamples.Run();
CloningAndOwnershipSamples.Run();
ModelShapesSamples.Run();
UiFrameworksSamples.Run();
VerifiedSamples.Run();
TutorialFirstEditSamples.Run();
LayeredSettingsSamples.Run();
PartialUpdateSamples.Run();
EditSessionGroupsDocsSamples.Run();
EditSessionStateDocsSamples.Run();

Console.WriteLine("SparseTypes docs consumer passed.");

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
