using System.Collections.Immutable;
using System.Reflection;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

// Verify relocation rewrites avoid literals/comments and preserve colliding
// member names, including through the product generator.
public sealed class RelocatedRewriteSafetyTests
{
    private const string ChildName = "global::Ns.Child";

    private static SparseGeneratorConfig RelocatedConfig() =>
        (SparseGeneratorConfig)(
            typeof(SparseTypesGenerator)
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
}
