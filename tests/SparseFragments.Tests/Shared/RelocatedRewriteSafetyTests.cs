using System.Collections.Immutable;
using System.Reflection;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Regression tests pinning string-rewrite safety for relocation (reloc-1
// review hardening, #190/#191). Relocation rewrites generated sources with
// text replacement; the same text inside string literals or comments must
// never be rewritten, and member names colliding with rewritten identifiers
// must keep working. The synthetic cases below feed crafted sources straight
// into the rewrite; the product-generator cases in RelocatedUiPlacementTests
// pin the end-to-end behavior.
public sealed class RelocatedRewriteSafetyTests
{
    private const string ChildName = "global::Ns.Child";

    private static SparseGeneratorConfig RelocatedConfig() =>
        (SparseGeneratorConfig)(
            typeof(SparseFragmentsGenerator)
                .GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null)
            ?? throw new InvalidOperationException("Missing generator configuration.")
        );

    private static SparseTypeModel ChildType() =>
        new(
            ChildName,
            ChildName,
            ChildName,
            IsReferenceType: true,
            IsFragmentModel: true,
            PocoCloneHelperName: null
        );

    private static ImmutableArray<SparseMemberModel> ChildMembers(
        string? attributeExpressions = null
    ) =>
        ImmutableArray.Create(
            new SparseMemberModel(
                0,
                new SparsePropertyModel(
                    "Child",
                    ChildType(),
                    JsonPropertyName: "Child",
                    AttributeExpressions: attributeExpressions
                ),
                ChildType(),
                SparseMergeModes.Replace,
                SparseCollectionInfo.Unsupported,
                null,
                null,
                ChildIsStructural: false,
                ChildIsReferenceType: true
            )
        );

    private static string Relocate(
        string source,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config
    ) =>
        SparseModelImplementationEmitter.RelocateUiReferences(
            source,
            "Parent",
            members,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            config,
            CancellationToken.None
        );

    [Test]
    public void ChildReferencesRewriteCodeButNotLiteralsOrComments()
    {
        var config = RelocatedConfig();
        var members = ChildMembers();
        var container = SparseGeneratedPlacement.GetContainerForQualifiedName(
            ChildName,
            CancellationToken.None
        );
        var root = SparseGeneratedPlacement.TryGetImplementationNamespace(config);
        root.ShouldNotBeNull();
        var relocatedRef = "global::" + root + "." + container + ".Observable";
        var source =
            "var view = global::Ns.Child.Observable.Create(); // global::Ns.Child.Observable stays\n"
            + "var text = \"global::Ns.Child.Observable stays\";\n"
            + "/* global::Ns.Child.Observable stays */\n";
        var relocated = Relocate(source, members, config);
        relocated.ShouldContain(relocatedRef + ".Create()");
        relocated.ShouldContain("// global::Ns.Child.Observable stays");
        relocated.ShouldContain("\"global::Ns.Child.Observable stays\"");
        relocated.ShouldContain("/* global::Ns.Child.Observable stays */");
    }

    [Test]
    public void AttributeBridgeRewriteSkipsLiteralsAndComments()
    {
        var config = RelocatedConfig();
        const string expression = "new global::System.ObsoleteAttribute()";
        var members = ChildMembers(attributeExpressions: expression);
        var legacy = "new global::System.Attribute[] { " + expression + " }";
        var source =
            "var first = "
            + legacy
            + ";\n"
            + "var text = \""
            + legacy
            + "\";\n"
            + "// "
            + legacy
            + "\n";
        var relocated = Relocate(source, members, config);
        relocated.ShouldContain("var first = Parent.__SparseAttributes_0();");
        relocated.ShouldContain("var text = \"" + legacy + "\";");
        relocated.ShouldContain("// " + legacy);
    }

    [Test]
    public void ReceiverRewriteSkipsLiteralsAndComments()
    {
        var source =
            "var name = this.Note; // this.Note stays\n"
            + "var text = \"this.Note and __model.Note stay\";\n"
            + "__Raise(\"Note\");\n"
            + "var target = __model.Note;\n"
            + "__onChanged?.Invoke();\n"
            + "__onRawModelAccess?.Invoke();\n";
        var rewritten = SparseDescriptorFactoryEmitter.RewriteReceiver(
            source,
            CancellationToken.None
        );
        rewritten.ShouldContain("var name = observable.Note;");
        rewritten.ShouldContain("// this.Note stays");
        rewritten.ShouldContain("\"this.Note and __model.Note stay\"");
        rewritten.ShouldContain("observable.__Raise(\"Note\");");
        rewritten.ShouldContain("var target = observable.__model.Note;");
        rewritten.ShouldContain("observable.__onChanged?.Invoke();");
        rewritten.ShouldContain("observable.__onRawModelAccess?.Invoke();");
    }
}
