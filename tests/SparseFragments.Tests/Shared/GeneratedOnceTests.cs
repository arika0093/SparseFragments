using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Unit coverage for the capability-driven Generated-Once plane (issue
// #178): deterministic capability sets, prerequisite validation, stable
// naming, and product-neutral rendering. Generator-driver and
// cross-assembly conformance live in the #180 suites.
public sealed class GeneratedOnceTests
{
    private static SparseEmissionFeatures Standalone() => SparseEmissionFeatures.Standalone;

    private static SparseTypeModel ScalarType(string name) =>
        new(name, name, name, IsReferenceType: true, IsFragmentModel: false, null);

    private static SparseMemberModel ScalarMember(int id, string name)
    {
        var property = new SparsePropertyModel(
            name,
            ScalarType("global::System.String?"),
            JsonPropertyName: name
        );
        return new SparseMemberModel(
            id,
            property,
            null,
            SparseMergeModes.Replace,
            SparseCollectionInfo.Unsupported,
            null,
            null,
            false,
            true
        );
    }

    private static SparseMemberModel ListMember(int id, string name)
    {
        var element = new SparseTypeModel(
            "string",
            "string",
            "string",
            IsReferenceType: true,
            IsFragmentModel: false,
            null,
            UsesDefaultScalarEquality: true
        );
        var property = new SparsePropertyModel(
            name,
            ScalarType("global::System.Collections.Generic.List<string>"),
            JsonPropertyName: name
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            element,
            null,
            null,
            SparseCollectionSemantic.ScalarSequence,
            ImmutableArray<string>.Empty,
            null,
            SparseKeyKind.None
        );
        return new SparseMemberModel(
            id,
            property,
            null,
            SparseMergeModes.Append,
            collection,
            null,
            null,
            false,
            true
        );
    }

    private static SparseMemberModel KeyedMember(int id, string name)
    {
        var element = ScalarType("global::Ns.Item");
        var property = new SparsePropertyModel(
            name,
            ScalarType("global::System.Collections.Generic.List<global::Ns.Item>"),
            JsonPropertyName: name
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            element,
            null,
            "System.Collections.Generic.List<T>",
            SparseCollectionSemantic.KeyedSequence,
            ImmutableArray.Create("Id"),
            "global::System.Int32",
            SparseKeyKind.Property
        );
        return new SparseMemberModel(
            id,
            property,
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            false,
            true
        );
    }

    private static SparseGenerationAnalysis Analysis(
        bool isStruct,
        ImmutableArray<SparseMemberModel> members
    ) =>
        new(
            new SparseModelInfo(
                "Model",
                "global::Ns.Model",
                "Ns",
                false,
                isStruct,
                false,
                "Ns.Model.SparseFragments.g.cs",
                null
            ),
            members,
            ImmutableArray<SparsePocoCloneModel>.Empty,
            ImmutableArray<SparseStructuralModel>.Empty,
            ImmutableArray<SparseGeneratorDiagnostic>.Empty
        );

    private static SparseGeneratedOncePlan Aggregate(
        params ImmutableArray<SparseMemberModel>[] roots
    )
    {
        var analyses = roots.Select(members => Analysis(false, members)).ToImmutableArray();
        return SparseGeneratedCapabilityPlanner.Aggregate(
            analyses,
            ImmutableArray<SparsePromotedModel>.Empty,
            Standalone(),
            true,
            true,
            CancellationToken.None
        );
    }

    [Test]
    public void ScalarClassRequestsOnlyEditSession()
    {
        var plan = Aggregate(ImmutableArray.Create(ScalarMember(0, "Name")));
        plan.Capabilities.ShouldBe(SparseGeneratedCapability.EditSession);
    }

    [Test]
    public void StructRequestsNoEditSession()
    {
        var analyses = ImmutableArray.Create(
            Analysis(true, ImmutableArray.Create(ScalarMember(0, "Name")))
        );
        var plan = SparseGeneratedCapabilityPlanner.Aggregate(
            analyses,
            ImmutableArray<SparsePromotedModel>.Empty,
            Standalone(),
            true,
            true,
            CancellationToken.None
        );
        plan.Capabilities.ShouldBe(SparseGeneratedCapability.None);
    }

    [Test]
    public void CollectionsRequestCloneAndAdapters()
    {
        var plan = Aggregate(ImmutableArray.Create(ListMember(1, "Tags")));
        plan.Has(SparseGeneratedCapability.CloneHelpers).ShouldBeTrue();
        plan.Has(SparseGeneratedCapability.ReadOnlyAdapters).ShouldBeTrue();
        plan.Has(SparseGeneratedCapability.RemovalIndex).ShouldBeFalse();
    }

    [Test]
    public void KeyedCollectionsRequestRemovalIndex()
    {
        var plan = Aggregate(ImmutableArray.Create(KeyedMember(2, "Items")));
        plan.Has(SparseGeneratedCapability.RemovalIndex).ShouldBeTrue();
        plan.Has(SparseGeneratedCapability.CloneHelpers).ShouldBeTrue();
    }

    [Test]
    public void NoModelRequestsNothing()
    {
        var plan = SparseGeneratedOncePlan.Empty;
        plan.Capabilities.ShouldBe(SparseGeneratedCapability.None);
        SparseGeneratedCapabilityPlanner.Validate(plan, Standalone()).ShouldBeEmpty();
    }

    [Test]
    public void AggregationIsOrderIndependent()
    {
        var scalar = ImmutableArray.Create(ScalarMember(0, "Name"));
        var keyed = ImmutableArray.Create(KeyedMember(2, "Items"));
        Aggregate(scalar, keyed).ShouldBe(Aggregate(keyed, scalar));
    }

    [Test]
    public void MissingPrerequisitesAreActionable()
    {
        var plan = new SparseGeneratedOncePlan(
            SparseGeneratedCapability.EditSession
                | SparseGeneratedCapability.CloneHelpers
                | SparseGeneratedCapability.ReadOnlyAdapters
                | SparseGeneratedCapability.RemovalIndex,
            false,
            true
        );
        var features = new SparseEmissionFeatures(
            EmitFragment: false,
            EmitPatch: false,
            EmitChangeSet: false,
            EmitChangePayload: false,
            EmitObservable: false,
            EmitJsonConverters: false
        );
        var errors = SparseGeneratedCapabilityPlanner.Validate(plan, features);
        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("EditSession"));
        errors.ShouldContain(e => e.Contains("CloneHelpers"));
        errors.ShouldContain(e => e.Contains("ReadOnlyAdapters"));
        errors.ShouldContain(e => e.Contains("RemovalIndex"));
    }

    [Test]
    public void HintNamesAreUniqueAcrossProducts()
    {
        var first = SparseGeneratedOnceEmitter.CloneHelpersHintName("Acme.Generated");
        var second = SparseGeneratedOnceEmitter.CloneHelpersHintName("Other.Generated");
        first.ShouldNotBe(second);
        SparseGeneratedOnceEmitter
            .ReadOnlyAdaptersHintName("Acme.Generated")
            .ShouldNotBe(SparseGeneratedOnceEmitter.RemovalIndexHintName("Acme.Generated"));
        SparseGeneratedOnceEmitter
            .CloneHelpersHintName("Acme.Generated")
            .ShouldNotBe(SparseGeneratedOnceEmitter.ReadOnlyAdaptersHintName("Acme.Generated"));
    }

    [Test]
    public void DownstreamHelpersUseNoStandaloneRuntime()
    {
        var clone = SparseGeneratedOnceEmitter.RenderCloneHelpers(
            "Downstream.Generated",
            false,
            true,
            CancellationToken.None
        );
        var adapters = SparseGeneratedOnceEmitter.RenderReadOnlyAdapters(
            "Downstream.Generated",
            CancellationToken.None
        );
        var removal = SparseGeneratedOnceEmitter.RenderRemovalIndex(
            "Downstream.Generated",
            CancellationToken.None
        );
        foreach (var source in new[] { clone, adapters, removal })
        {
            source.ShouldContain("namespace Downstream.Generated");
            source.ShouldContain("internal ");
            source.ShouldNotContain("global::SparseFragments");
            source.ShouldNotContain("SparseFragments.");
        }

        clone.ShouldContain("internal static class SparseCloneHelpers");
        adapters.ShouldContain("internal sealed class SparseReadOnlyListAdapter");
        adapters.ShouldContain("internal sealed class SparseReadOnlyDictionaryAdapter");
        adapters.ShouldContain("internal sealed class SparseReadOnlyDictionaryEntriesAdapter");
        removal.ShouldContain("internal static class SparseRemovalIndex<TKey>");
    }

    [Test]
    public void RenderedHelpersAreDeterministicAndCSharp9Clean()
    {
        string RenderClone() =>
            SparseGeneratedOnceEmitter.RenderCloneHelpers(
                "Downstream.Generated",
                true,
                false,
                CancellationToken.None
            );
        string RenderAdapters() =>
            SparseGeneratedOnceEmitter.RenderReadOnlyAdapters(
                "Downstream.Generated",
                CancellationToken.None
            );
        string RenderRemoval() =>
            SparseGeneratedOnceEmitter.RenderRemovalIndex(
                "Downstream.Generated",
                CancellationToken.None
            );
        RenderClone().ShouldBe(RenderClone());
        RenderAdapters().ShouldBe(RenderAdapters());
        RenderRemoval().ShouldBe(RenderRemoval());

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        foreach (var source in new[] { RenderClone(), RenderAdapters(), RenderRemoval() })
        {
            CSharpSyntaxTree
                .ParseText(source, parseOptions)
                .GetDiagnostics()
                .Where(static diagnostic =>
                    diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                )
                .ShouldBeEmpty();
            source.ShouldNotContain("record class ");
        }
    }
}
