using System.Collections.Immutable;
using System.Threading;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

// Emitter contracts for issue #270: the session Paths surface and the
// collection typed-value contract stay opt-in so downstream products without
// the contracts keep byte-identical output (isolated consumer probe).
public sealed class SessionPathsEmissionTests
{
    private static SparseTypeModel ScalarType(string name) =>
        new(
            name,
            name,
            name,
            IsReferenceType: name.Contains("String", StringComparison.Ordinal),
            IsFragmentModel: false,
            null
        );

    private static SparseMemberModel ScalarMember(int id, string name, string typeName)
    {
        var property = new SparsePropertyModel(
            name,
            ScalarType(typeName),
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: false,
            JsonPropertyName: name,
            HasExplicitJsonPropertyName: false,
            JsonIgnoreCondition: 0
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

    private static SparseMemberModel KeyedMember()
    {
        var element = ScalarType("global::Ns.Item");
        var list = ScalarType("global::System.Collections.Generic.List<global::Ns.Item>");
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
            4,
            new SparsePropertyModel("Items", list, JsonPropertyName: "Items"),
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            false,
            true
        );
    }

    private static SparseFragmentPatchEmitter.SparsePatchDialect PlainDialect() =>
        new(
            "global::Downstream.",
            "__sparse_whole",
            "__SparseMembersEmpty",
            static member => "__sparse_patch_member_" + member.Id,
            static _ => string.Empty,
            "Apply",
            false,
            "global::Downstream.Runtime",
            "global::Downstream.Conflict",
            "global::Downstream.ConflictKind",
            static payload => "global::Downstream.Rebase<" + payload + ">",
            static member => "Patch",
            static member => "global::Downstream.Delta_" + member.Id,
            "DownstreamInternal"
        );

    private static SparseFragmentPatchEmitter.SparsePatchDialect TypedPathsDialect() =>
        PlainDialect() with
        {
            PathType = "global::Downstream.Path",
            PathIsValueType = true,
            TypedPathInterfaceType = "global::Downstream.ITypedPath",
        };

    private static SparseGeneratorConfig PlainConfig(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) =>
        new(
            ModelAttributeMetadataName: "Downstream.ModelAttribute",
            IgnoreAttributeMetadataName: "Downstream.IgnoreAttribute",
            RedactBeforeAttributeMetadataName: "Downstream.RedactBeforeAttribute",
            MergeAttributeMetadataName: "Downstream.MergeAttribute",
            MergeStrategyBaseMetadataName: "Downstream.MergeStrategy<T>",
            CloneReferenceSafeAttributeMetadataName: "Downstream.CloneSafeAttribute",
            KeyAttributeMetadataName: "Downstream.IdentityAttribute",
            MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
            DiagnosticIds: new SparseDiagnosticIdMap(
                "DWN001",
                "DWN002",
                "DWN003",
                "DWN004",
                "DWN005",
                "DWN006",
                "DWN007",
                "DWN008",
                "DWN009",
                "DWN010",
                "DWN011",
                "DWN013",
                "DWN014",
                "DWN017",
                "DWN019",
                "DWN021",
                "DWN022",
                "DWN023"
            ),
            HintNameSuffix: ".Downstream.g.cs",
            PromotedHintNameSuffix: ".DownstreamPromoted.g.cs",
            StructuralHostPrefix: "__DownstreamHost_",
            RuntimeDialect: new(
                "global::Downstream.",
                "global::Downstream.Optional",
                "global::Downstream.MergeStrategy",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "__downstream_merge_"
            ),
            PatchDialect: dialect
        );

    private static SparseModelInfo ReadModel() =>
        new(
            "ReadModel",
            "global::Ns.ReadModel",
            "Ns",
            IsGlobalNamespace: false,
            IsStruct: false,
            IsRecord: false,
            HintName: "ReadModel.g.cs",
            Constructor: null,
            IgnoredSettablePropertyNames: ImmutableArray<string>.Empty
        );

    private static string BuildPaths(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        ImmutableArray<SparseMemberModel> members
    )
    {
        using var code = new SharedIndentedBuilder(CancellationToken.None);
        SparsePathBuilderEmitter.AppendPathsClass(code, "global::Ns.ReadModel", members, dialect);
        return code.ToString();
    }

    [Test]
    public void PathsSurfaceStaysDisabledWithoutContracts()
    {
        var config = PlainConfig(PlainDialect());

        SparseEditSessionPathsEmitter.HasPathsSurface(config, PlainDialect()).ShouldBeFalse();
        SparseEditSessionPathsEmitter
            .GetPathsInterfaceBase(
                ReadModel(),
                "global::Ns.ReadModel",
                config,
                PlainDialect(),
                CancellationToken.None
            )
            .ShouldBe(string.Empty);

        // No typed-path surface either: downstream output stays byte-identical.
        BuildPaths(PlainDialect(), ImmutableArray.Create(ScalarMember(0, "Label", "string")))
            .ShouldNotContain("ITypedPath");
    }

    [Test]
    public void CollectionBuilderCarriesTypedValueContract()
    {
        var text = BuildPaths(
            TypedPathsDialect(),
            ImmutableArray.Create(ScalarMember(0, "Label", "string"), KeyedMember())
        );

        // Key, At, dictionary, and set navigation keep their shapes.
        text.ShouldContain("Key(global::System.Int32 key)");
        text.ShouldContain("At(int index)");

        // The collection builder itself carries the collection value type for
        // future typed consumers, without breaking element navigation.
        text.ShouldContain("SparseItemsPaths : global::Downstream.ITypedPath<TRoot,");
        text.ShouldContain("UntypedPath => _path;");
        text.ShouldContain("CanWriteModelValueInPlace => false;");
        text.ShouldContain("WriteModelValueInPlace(");
    }

    [Test]
    public void PathsTypeResolvesNestedOrRelocated()
    {
        var model = ReadModel();

        SparseEditSessionPathsEmitter
            .GetPathsType(model, PlainConfig(PlainDialect()), CancellationToken.None)
            .ShouldBe("SparsePaths<global::Ns.ReadModel>");

        var relocated = PlainConfig(PlainDialect()) with
        {
            GeneratedImplementationNamespace = "Downstream.Generated",
        };
        SparseEditSessionPathsEmitter
            .GetPathsType(model, relocated, CancellationToken.None)
            .ShouldContain("global::Downstream.Generated.");
        SparseEditSessionPathsEmitter
            .GetPathsType(model, relocated, CancellationToken.None)
            .ShouldContain(".SparsePaths<global::Ns.ReadModel>");
    }

    [Test]
    public void PathsMembersEmitOnlyWithProviderContract()
    {
        var model = ReadModel();
        var without = PlainConfig(TypedPathsDialect());
        using (var code = new SharedIndentedBuilder(CancellationToken.None))
        {
            SparseEditSessionPathsEmitter.AppendPathsProperty(
                code,
                model,
                "global::Ns.ReadModel",
                without,
                TypedPathsDialect()
            );
            code.ToString().ShouldNotContain("Paths => __paths;");
        }

        var with = without with
        {
            EditSessionWithPathsInterfaceMetadataName = "Downstream.IEditSessionWithPaths",
        };
        SparseEditSessionPathsEmitter.HasPathsSurface(with, TypedPathsDialect()).ShouldBeTrue();
        SparseEditSessionPathsEmitter
            .GetPathsInterfaceBase(
                model,
                "global::Ns.ReadModel",
                with,
                TypedPathsDialect(),
                CancellationToken.None
            )
            .ShouldContain("Downstream.IEditSessionWithPaths<global::Ns.ReadModel,");
        using (var code = new SharedIndentedBuilder(CancellationToken.None))
        {
            SparseEditSessionPathsEmitter.AppendPathsProperty(
                code,
                model,
                "global::Ns.ReadModel",
                with,
                TypedPathsDialect()
            );
            SparseEditSessionPathsEmitter.AppendPathsField(
                code,
                model,
                "global::Ns.ReadModel",
                with,
                TypedPathsDialect()
            );
            var text = code.ToString();
            text.ShouldContain("Paths => __paths;");
            text.ShouldContain("__paths = new ");
            text.ShouldContain(".Root(typeof(global::Ns.ReadModel))");
        }
    }
}
