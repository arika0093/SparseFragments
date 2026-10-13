using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

/// <summary>Certifies explicit JSON feature overrides against probed STJ surfaces (issue #264).</summary>
/// <remarks>
/// A downstream product disables JSON by selecting out of the
/// <c>EmitJsonConverters</c> family; the effective choice must stay off even
/// when the compilation offers the full API set. Absent or partial surfaces
/// suppress JSON even when the family is requested, so emitters never see an
/// incoherent plan.
/// </remarks>
public sealed class StjEmissionOverrideTests
{
    private static SparseGeneratorConfig MinimalConfig(
        SparseEmissionFeatures? emissionFeatures,
        SparseStjCapabilities? stjCapabilities
    ) =>
        new(
            ModelAttributeMetadataName: "SparseTypes.SparseModelAttribute",
            IgnoreAttributeMetadataName: "SparseTypes.SparseIgnoreAttribute",
            RedactBeforeAttributeMetadataName: "SparseTypes.SparseRedactBeforeAttribute",
            MergeAttributeMetadataName: "SparseTypes.SparseMergeAttribute",
            MergeStrategyBaseMetadataName: "SparseTypes.FragmentMergeStrategy<T>",
            CloneReferenceSafeAttributeMetadataName: "SparseTypes.SparseCloneReferenceSafeAttribute",
            KeyAttributeMetadataName: "SparseTypes.SparseKeyAttribute",
            MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
            DiagnosticIds: new SparseDiagnosticIdMap(
                "SPF001",
                "SPF002",
                "SPF003",
                "SPF004",
                "SPF005",
                "SPF006",
                "SPF007",
                "SPF008",
                "SPF009",
                "SPF010",
                "SPF011",
                "SPF013",
                "SPF014",
                "SPF017",
                "SPF019",
                "SPF021",
                "SPF022",
                "SPF023"
            ),
            HintNameSuffix: ".SparseTypes.g.cs",
            PromotedHintNameSuffix: ".SparsePromoted.g.cs",
            StructuralHostPrefix: "__SparseStructural_",
            EmissionFeatures: emissionFeatures,
            StjCapabilities: stjCapabilities
        );

    [Test]
    public void DefaultSelectionEmitsJsonWithFullSurface()
    {
        MinimalConfig(emissionFeatures: null, stjCapabilities: SparseStjCapabilities.All)
            .EffectiveEmitJson.ShouldBeTrue();
    }

    [Test]
    public void ExplicitJsonOptOutSuppressesBridgeEvenWithFullSurface()
    {
        MinimalConfig(
            emissionFeatures: new SparseEmissionFeatures(EmitJsonConverters: false),
            stjCapabilities: SparseStjCapabilities.All
        )
            .EffectiveEmitJson.ShouldBeFalse();
    }

    [Test]
    public void PartialSurfaceSuppressesJsonEvenWhenRequested()
    {
        MinimalConfig(
            emissionFeatures: null,
            stjCapabilities: SparseStjCapabilities.All with
            {
                HasJsonPropertyOrder = false,
            }
        )
            .EffectiveEmitJson.ShouldBeFalse();
    }

    [Test]
    public void AbsentSurfaceSuppressesJsonEvenWhenRequested()
    {
        MinimalConfig(emissionFeatures: null, stjCapabilities: SparseStjCapabilities.None)
            .EffectiveEmitJson.ShouldBeFalse();
    }
}
