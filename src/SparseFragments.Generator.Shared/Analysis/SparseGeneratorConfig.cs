using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

internal sealed record SparseGeneratorConfig
{
    public SparseGeneratorConfig(
        string ModelAttributeMetadataName,
        string IgnoreAttributeMetadataName,
        string MergeAttributeMetadataName,
        string MergeStrategyBaseMetadataName,
        string CloneReferenceSafeAttributeMetadataName,
        string KeyAttributeMetadataName,
        string KeyedInterfaceMetadataName,
        string KeyPropertyName,
        SparseMergeModeMap MergeModeMap,
        SparseDiagnosticIdMap DiagnosticIds,
        string HintNameSuffix,
        string PromotedHintNameSuffix,
        string StructuralHostPrefix,
        SparseStructuralPolicy StructuralPolicy = SparseStructuralPolicy.AtomicReplace,
        ImmutableArray<string> ReservedGeneratedNames = default,
        SparseRuntimeDialect? RuntimeDialect = null,
        SparseFragmentPatchEmitter.SparsePatchDialect? PatchDialect = null
    )
    {
        this.ModelAttributeMetadataName = ModelAttributeMetadataName;
        this.IgnoreAttributeMetadataName = IgnoreAttributeMetadataName;
        this.MergeAttributeMetadataName = MergeAttributeMetadataName;
        this.MergeStrategyBaseMetadataName = MergeStrategyBaseMetadataName;
        this.CloneReferenceSafeAttributeMetadataName = CloneReferenceSafeAttributeMetadataName;
        this.StructuralPolicy = StructuralPolicy;
        this.KeyAttributeMetadataName = KeyAttributeMetadataName;
        this.KeyedInterfaceMetadataName = KeyedInterfaceMetadataName;
        this.KeyPropertyName = KeyPropertyName;
        this.MergeModeMap = MergeModeMap;
        this.ReservedGeneratedNames = ReservedGeneratedNames;
        this.DiagnosticIds = DiagnosticIds;
        this.HintNameSuffix = HintNameSuffix;
        this.PromotedHintNameSuffix = PromotedHintNameSuffix;
        this.StructuralHostPrefix = StructuralHostPrefix;
        this.RuntimeDialect = RuntimeDialect;
        this.PatchDialect = PatchDialect;
    }

    public string ModelAttributeMetadataName { get; init; }

    public string IgnoreAttributeMetadataName { get; init; }

    public string MergeAttributeMetadataName { get; init; }

    public string MergeStrategyBaseMetadataName { get; init; }

    public string CloneReferenceSafeAttributeMetadataName { get; init; }

    public SparseStructuralPolicy StructuralPolicy { get; init; }

    public string KeyAttributeMetadataName { get; init; }

    public string KeyedInterfaceMetadataName { get; init; }

    public string KeyPropertyName { get; init; }

    public SparseMergeModeMap MergeModeMap { get; init; }

    public ImmutableArray<string> ReservedGeneratedNames { get; init; }

    public SparseDiagnosticIdMap DiagnosticIds { get; init; }

    public string HintNameSuffix { get; init; }

    public string PromotedHintNameSuffix { get; init; }

    public string StructuralHostPrefix { get; init; }

    public SparseRuntimeDialect? RuntimeDialect { get; init; }

    public SparseFragmentPatchEmitter.SparsePatchDialect? PatchDialect { get; init; }

    public SparseMergeModeMap EffectiveMergeModeMap => MergeModeMap;

    public SparseDiagnosticIdMap EffectiveDiagnosticIds => DiagnosticIds;

    public ImmutableArray<string> EffectiveReservedGeneratedNames =>
        ReservedGeneratedNames.IsDefault ? ImmutableArray<string>.Empty : ReservedGeneratedNames;
}

internal sealed record SparseDiagnosticIdMap
{
    public SparseDiagnosticIdMap(
        string MustBePartial,
        string UnsupportedModel,
        string MissingConstructor,
        string InvalidMergeStrategy,
        string UnsupportedMerge,
        string UnsupportedRequired,
        string UnsupportedStructural,
        string UnsupportedClone,
        string GeneratedNameCollision,
        string IncompatiblePromotedModel,
        string UnkeyedStructuralSequence,
        string ConflictingKeyMechanisms,
        string MultiplePropertyKeys,
        string InvalidKeyAttributeShape,
        string MissingKeyComponent,
        string DuplicateKeyComponent,
        string InaccessibleKeyProperty,
        string NullableKey,
        string UnsupportedKeyShape,
        string InvalidKeyedInterface,
        string DuplicateJsonPropertyName,
        string SparseIgnoreOnKey,
        string SparseIgnoreUnsupportedProperty
    )
    {
        this.MustBePartial = MustBePartial;
        this.UnsupportedModel = UnsupportedModel;
        this.MissingConstructor = MissingConstructor;
        this.InvalidMergeStrategy = InvalidMergeStrategy;
        this.UnsupportedMerge = UnsupportedMerge;
        this.UnsupportedRequired = UnsupportedRequired;
        this.UnsupportedStructural = UnsupportedStructural;
        this.UnsupportedClone = UnsupportedClone;
        this.GeneratedNameCollision = GeneratedNameCollision;
        this.IncompatiblePromotedModel = IncompatiblePromotedModel;
        this.UnkeyedStructuralSequence = UnkeyedStructuralSequence;
        this.ConflictingKeyMechanisms = ConflictingKeyMechanisms;
        this.MultiplePropertyKeys = MultiplePropertyKeys;
        this.InvalidKeyAttributeShape = InvalidKeyAttributeShape;
        this.MissingKeyComponent = MissingKeyComponent;
        this.DuplicateKeyComponent = DuplicateKeyComponent;
        this.InaccessibleKeyProperty = InaccessibleKeyProperty;
        this.NullableKey = NullableKey;
        this.UnsupportedKeyShape = UnsupportedKeyShape;
        this.InvalidKeyedInterface = InvalidKeyedInterface;
        this.DuplicateJsonPropertyName = DuplicateJsonPropertyName;
        this.SparseIgnoreOnKey = SparseIgnoreOnKey;
        this.SparseIgnoreUnsupportedProperty = SparseIgnoreUnsupportedProperty;
    }

    public string MustBePartial { get; init; }

    public string UnsupportedModel { get; init; }

    public string MissingConstructor { get; init; }

    public string InvalidMergeStrategy { get; init; }

    public string UnsupportedMerge { get; init; }

    public string UnsupportedRequired { get; init; }

    public string UnsupportedStructural { get; init; }

    public string UnsupportedClone { get; init; }

    public string GeneratedNameCollision { get; init; }

    public string IncompatiblePromotedModel { get; init; }

    public string UnkeyedStructuralSequence { get; init; }

    public string ConflictingKeyMechanisms { get; init; }

    public string MultiplePropertyKeys { get; init; }

    public string InvalidKeyAttributeShape { get; init; }

    public string MissingKeyComponent { get; init; }

    public string DuplicateKeyComponent { get; init; }

    public string InaccessibleKeyProperty { get; init; }

    public string NullableKey { get; init; }

    public string UnsupportedKeyShape { get; init; }

    public string InvalidKeyedInterface { get; init; }

    public string DuplicateJsonPropertyName { get; init; }

    public string SparseIgnoreOnKey { get; init; }

    public string SparseIgnoreUnsupportedProperty { get; init; }
}

internal sealed record SparseRuntimeDialect
{
    public SparseRuntimeDialect(
        string Namespace,
        string OptionalType,
        string MergeStrategyType,
        string ReferenceComparer,
        string ValueComparer,
        string CollectionMerger,
        string CollectionRebase,
        string MergeStrategyFieldPrefix
    )
    {
        this.Namespace = Namespace;
        this.OptionalType = OptionalType;
        this.MergeStrategyType = MergeStrategyType;
        this.ReferenceComparer = ReferenceComparer;
        this.ValueComparer = ValueComparer;
        this.CollectionMerger = CollectionMerger;
        this.CollectionRebase = CollectionRebase;
        this.MergeStrategyFieldPrefix = MergeStrategyFieldPrefix;
    }

    public string Namespace { get; init; }

    public string OptionalType { get; init; }

    public string MergeStrategyType { get; init; }

    public string ReferenceComparer { get; init; }

    public string ValueComparer { get; init; }

    public string CollectionMerger { get; init; }

    public string CollectionRebase { get; init; }

    public string MergeStrategyFieldPrefix { get; init; }
}

internal readonly record struct SparseMergeModeMap
{
    public SparseMergeModeMap(
        int Default,
        int Replace,
        int Deep,
        int Append,
        int SetUnion,
        int Custom
    )
    {
        this.Default = Default;
        this.Replace = Replace;
        this.Deep = Deep;
        this.Append = Append;
        this.SetUnion = SetUnion;
        this.Custom = Custom;
    }

    public int Default { get; init; }

    public int Replace { get; init; }

    public int Deep { get; init; }

    public int Append { get; init; }

    public int SetUnion { get; init; }

    public int Custom { get; init; }

    public int Normalize(int input)
    {
        var matches = 0;
        var normalized = int.MaxValue;
        Match(Default, SparseMergeModes.Default);
        Match(Replace, SparseMergeModes.Replace);
        Match(Deep, SparseMergeModes.Deep);
        Match(Append, SparseMergeModes.Append);
        Match(SetUnion, SparseMergeModes.SetUnion);
        Match(Custom, SparseMergeModes.Custom);
        return matches == 1 ? normalized : int.MaxValue;

        void Match(int configured, int semantic)
        {
            if (configured == input)
            {
                matches++;
                normalized = semantic;
            }
        }
    }
}

internal static class SparseMergeModes
{
    public const int Default = 0;
    public const int Replace = 1;
    public const int Deep = 2;
    public const int Append = 3;
    public const int SetUnion = 4;
    public const int Custom = 5;

    /// <summary>Whether the mode deep-merges a nested model value.</summary>
    public static bool IsDeepMerge(int mode, bool hasChild) =>
        (mode == Deep || mode == Default) && hasChild;
}
