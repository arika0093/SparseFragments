using System.Collections.Immutable;
using System.Reflection;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Stage-1 tests for the facade/implementation separation (issue #176):
// central placement resolution, explicit product-neutral policy, and
// multi-source result capability. Later stages relocate types through this
// resolver; this suite pins naming stability and collision safety first.
public sealed class GeneratedPlacementTests
{
    private static SparseModelInfo Model(
        string @namespace,
        string typeName,
        bool isGlobalNamespace = false,
        bool isPublic = true
    ) =>
        new(
            typeName,
            typeName,
            @namespace,
            isGlobalNamespace,
            false,
            false,
            "hint.SparseFragments.g.cs",
            null,
            IsPublic: isPublic
        );

    private static SparseGeneratorConfig Config(string? implementationNamespace) =>
        new(
            ModelAttributeMetadataName: "ModelAttribute",
            IgnoreAttributeMetadataName: "IgnoreAttribute",
            RedactBeforeAttributeMetadataName: "RedactBeforeAttribute",
            MergeAttributeMetadataName: "MergeAttribute",
            MergeStrategyBaseMetadataName: "MergeStrategyBase",
            CloneReferenceSafeAttributeMetadataName: "CloneSafeAttribute",
            KeyAttributeMetadataName: "KeyAttribute",
            MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
            DiagnosticIds: new SparseDiagnosticIdMap(
                "T001",
                "T002",
                "T003",
                "T004",
                "T005",
                "T006",
                "T007",
                "T008",
                "T009",
                "T010",
                "T011",
                "T013",
                "T014",
                "T017",
                "T019",
                "T021",
                "T022",
                "T023"
            ),
            HintNameSuffix: ".Downstream.g.cs",
            PromotedHintNameSuffix: ".DownstreamPromoted.g.cs",
            StructuralHostPrefix: "__DownstreamHost_",
            GeneratedImplementationNamespace: implementationNamespace
        );

    [Test]
    public void ContainerIsDeterministicPerModelIdentity()
    {
        var model = Model("App.Orders", "Order");
        SparseGeneratedPlacement
            .GetImplementationContainer(model, CancellationToken.None)
            .ShouldBe(
                SparseGeneratedPlacement.GetImplementationContainer(model, CancellationToken.None)
            );
    }

    [Test]
    public void SanitizedCollisionsStayDistinct()
    {
        // "A-B" and "A.B" sanitize identically; the stable hash separates them.
        var hyphen = Model("App", "A-B");
        var dotted = Model("App", "A.B");
        var first = SparseGeneratedPlacement.GetImplementationContainer(
            hyphen,
            CancellationToken.None
        );
        var second = SparseGeneratedPlacement.GetImplementationContainer(
            dotted,
            CancellationToken.None
        );
        first.ShouldNotBe(second);
    }

    [Test]
    public void GlobalAndInternalModelsShareTheScheme()
    {
        var global = Model(string.Empty, "Order", isGlobalNamespace: true);
        var nested = Model("App", "Order");
        var internalModel = Model("App", "Order", isPublic: false);
        var globalContainer = SparseGeneratedPlacement.GetImplementationContainer(
            global,
            CancellationToken.None
        );
        var nestedContainer = SparseGeneratedPlacement.GetImplementationContainer(
            nested,
            CancellationToken.None
        );
        globalContainer.ShouldNotBe(nestedContainer);
        // Accessibility never feeds naming.
        SparseGeneratedPlacement
            .GetImplementationContainer(internalModel, CancellationToken.None)
            .ShouldBe(nestedContainer);
    }

    [Test]
    public void ParentChildReferencesShareTheChildContainer()
    {
        // Blocking review finding on reloc-1 (#190/#191): parent-side child
        // refs hash NonNullableName while the child hashes its own identity.
        // The old qualifier handling doubled top-level names and diverged for
        // nested models; both sides must normalize identically instead.
        ParentRefShouldMatchChildContainer(
            "Inner",
            "global::Ns.Outer.Inner",
            "Ns",
            isGlobalNamespace: false
        );
        ParentRefShouldMatchChildContainer(
            "Item",
            "global::Ns.Item",
            "Ns",
            isGlobalNamespace: false
        );
        ParentRefShouldMatchChildContainer(
            "Model",
            "global::Model",
            string.Empty,
            isGlobalNamespace: true
        );
    }

    private static void ParentRefShouldMatchChildContainer(
        string name,
        string modelTypeName,
        string @namespace,
        bool isGlobalNamespace
    )
    {
        var child = new SparseModelInfo(
            name,
            modelTypeName,
            @namespace,
            isGlobalNamespace,
            IsStruct: false,
            IsRecord: false,
            HintName: "hint.SparseFragments.g.cs",
            Constructor: null,
            IsPublic: true
        );
        SparseGeneratedPlacement
            .GetContainerForQualifiedName(modelTypeName, CancellationToken.None)
            .ShouldBe(
                SparseGeneratedPlacement.GetImplementationContainer(child, CancellationToken.None)
            );
    }

    [Test]
    public void CustomNamespaceHasNoProductFallback()
    {
        var model = Model("App", "Order");
        var config = Config("Acme.Generated");
        var qualified = SparseGeneratedPlacement.GetImplementationTypeName(
            model,
            config,
            "EditSession",
            CancellationToken.None
        );
        qualified.ShouldStartWith("Acme.Generated.");
        qualified.ShouldEndWith(".EditSession");
        qualified.ShouldNotContain("SparseFragments");
    }

    [Test]
    public void MissingNamespaceDisablesPlacement()
    {
        var model = Model("App", "Order");
        SparseGeneratedPlacement
            .GetImplementationTypeName(model, Config(null), "EditSession", CancellationToken.None)
            .ShouldBeNull();
        SparseGeneratedPlacement
            .GetImplementationTypeName(
                model,
                Config(string.Empty),
                "EditSession",
                CancellationToken.None
            )
            .ShouldBeNull();
    }

    [Test]
    public void UiTypeNamesDodgeMemberCollisions()
    {
        SparseGeneratedPlacement.ResolveUiTypeName("Observable", ["Name"]).ShouldBe("Observable");
        SparseGeneratedPlacement
            .ResolveUiTypeName("Observable", ["Observable"])
            .ShouldBe("SparseObservable");
        SparseGeneratedPlacement
            .ResolveUiTypeName("ReadOnlyView", ["ReadOnlyView", "SparseReadOnlyView"])
            .ShouldBe("SparseSparseReadOnlyView");
    }

    [Test]
    public void HintNamesAreStableAndDistinct()
    {
        var model = Model("App", "Order");
        SparseGeneratedPlacement.SurfaceHintName(model).ShouldBe("hint.SparseFragments.g.cs");
        var first = SparseGeneratedPlacement.ImplementationHintName(
            model,
            ".Implementation.g.cs",
            CancellationToken.None
        );
        var second = SparseGeneratedPlacement.ImplementationHintName(
            model,
            ".Implementation.g.cs",
            CancellationToken.None
        );
        first.ShouldBe(second);
        first.ShouldNotBe("hint.SparseFragments.g.cs");
        first.ShouldEndWith(".Implementation.g.cs");
    }

    [Test]
    public void ProductGeneratorDeclaresItsNamespaceExplicitly()
    {
        var configuration = (SparseGeneratorConfig)(
            typeof(SparseFragmentsGenerator)
                .GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null)
            ?? throw new InvalidOperationException("Missing generator configuration.")
        );
        configuration.GeneratedImplementationNamespace.ShouldBe("SparseFragments.Generated");
    }

    [Test]
    public void ResultsWithAdditionalSourcesCompareByValue()
    {
        var diagnostics = ImmutableArray<SparseGeneratorDiagnostic>.Empty;
        var extra = ImmutableArray.Create(
            new SparseGeneratedSource("b.Implementation.g.cs", "source")
        );
        var left = new SparseGenerationResult("a.g.cs", "surface", diagnostics, extra);
        var right = new SparseGenerationResult("a.g.cs", "surface", diagnostics, extra);
        left.Equals(right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
        new SparseGenerationResult("a.g.cs", "surface", diagnostics).Equals(left).ShouldBeFalse();
    }
}
