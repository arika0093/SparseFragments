using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

internal sealed record SparseGeneratorConfig
{
    public SparseGeneratorConfig(
        string ModelAttributeMetadataName,
        string IgnoreAttributeMetadataName,
        string RedactBeforeAttributeMetadataName,
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
        SparseFragmentPatchEmitter.SparsePatchDialect? PatchDialect = null,
        string? RebasePolicyAttributeMetadataName = null,
        string? RebasePolicyBaseMetadataName = null,
        SparseEmissionFeatures? EmissionFeatures = null,
        ImmutableArray<string> ProductExtensionNames = default,
        string? ComparisonAttributeMetadataName = null,
        string? EditSessionInterfaceMetadataName = null,
        SparseEditSessionDialect? EditSessionDialect = null,
        string? EditSessionModelAccessorInterfaceMetadataName = null,
        SparseDescriptorDialect? DescriptorDialect = null,
        string? GeneratedImplementationNamespace = null
    )
    {
        this.ModelAttributeMetadataName = ModelAttributeMetadataName;
        this.IgnoreAttributeMetadataName = IgnoreAttributeMetadataName;
        this.RedactBeforeAttributeMetadataName = RedactBeforeAttributeMetadataName;
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
        this.RebasePolicyAttributeMetadataName = RebasePolicyAttributeMetadataName;
        this.RebasePolicyBaseMetadataName = RebasePolicyBaseMetadataName;
        this.EmissionFeatures = EmissionFeatures;
        this.ProductExtensionNames = ProductExtensionNames;
        this.ComparisonAttributeMetadataName = ComparisonAttributeMetadataName;
        this.EditSessionInterfaceMetadataName = EditSessionInterfaceMetadataName;
        this.EditSessionDialect = EditSessionDialect;
        this.EditSessionModelAccessorInterfaceMetadataName =
            EditSessionModelAccessorInterfaceMetadataName;
        this.DescriptorDialect = DescriptorDialect;
        this.GeneratedImplementationNamespace = GeneratedImplementationNamespace;
    }

    public string ModelAttributeMetadataName { get; init; }

    public string IgnoreAttributeMetadataName { get; init; }

    public string RedactBeforeAttributeMetadataName { get; init; }

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

    /// <summary>Attribute marking a member-level rebase policy, or null when the product has none.</summary>
    public string? RebasePolicyAttributeMetadataName { get; init; }

    /// <summary>Rebase policy base type for validation, or null when the product has none.</summary>
    public string? RebasePolicyBaseMetadataName { get; init; }

    /// <summary>Feature families the owning generator opts into.</summary>
    public SparseEmissionFeatures? EmissionFeatures { get; init; }

    /// <summary>Product-declared type names appended through product extensions.</summary>
    public ImmutableArray<string> ProductExtensionNames { get; init; }

    /// <summary>Attribute defining a type-level equality comparer, or null when comparison rules are disabled.</summary>
    public string? ComparisonAttributeMetadataName { get; init; }

    /// <summary>Generic edit-session interface implemented by generated model sessions, or null when disabled.</summary>
    public string? EditSessionInterfaceMetadataName { get; init; }

    /// <summary>Product-owned names for the generated edit-session implementation.</summary>
    public SparseEditSessionDialect? EditSessionDialect { get; init; }

    /// <summary>
    /// Trusted model-accessor interface implemented by generated model sessions, or null when disabled.
    /// </summary>
    public string? EditSessionModelAccessorInterfaceMetadataName { get; init; }

    /// <summary>Product-owned descriptor contracts and runtime helper names, or null when disabled.</summary>
    public SparseDescriptorDialect? DescriptorDialect { get; init; }

    /// <summary>Explicit per-model implementation namespace, or null for single-file emission.</summary>
    /// <remarks>
    /// Stages that relocate generated machinery outside the attributed model
    /// require an explicit namespace from the owning generator. Shared code
    /// provides no implicit fallback.
    /// </remarks>
    public string? GeneratedImplementationNamespace { get; init; }

    public SparseMergeModeMap EffectiveMergeModeMap => MergeModeMap;

    public SparseDiagnosticIdMap EffectiveDiagnosticIds => DiagnosticIds;

    public ImmutableArray<string> EffectiveReservedGeneratedNames =>
        ReservedGeneratedNames.IsDefault ? ImmutableArray<string>.Empty : ReservedGeneratedNames;

    /// <summary>Effective feature selection, defaulting to the standalone set.</summary>
    public SparseEmissionFeatures EffectiveEmissionFeatures =>
        EmissionFeatures ?? SparseEmissionFeatures.Standalone;

    /// <summary>Effective product-declared type names, or empty when none are appended.</summary>
    public ImmutableArray<string> EffectiveProductExtensionNames =>
        ProductExtensionNames.IsDefault ? ImmutableArray<string>.Empty : ProductExtensionNames;
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
        string SparseIgnoreUnsupportedProperty,
        string InvalidRebasePolicy = "SPF027",
        string InvalidEmissionPlan = "SPF028",
        string UnknownProductMember = "SPF029",
        string InvalidComparisonStrategy = "SPF030"
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
        this.InvalidRebasePolicy = InvalidRebasePolicy;
        this.InvalidEmissionPlan = InvalidEmissionPlan;
        this.UnknownProductMember = UnknownProductMember;
        this.InvalidComparisonStrategy = InvalidComparisonStrategy;
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

    /// <summary>Diagnostic ID for an invalid custom rebase policy.</summary>
    public string InvalidRebasePolicy { get; init; }

    /// <summary>Diagnostic ID for an incoherent downstream emission plan.</summary>
    public string InvalidEmissionPlan { get; init; }

    /// <summary>Diagnostic ID for a product policy referencing an unknown member.</summary>
    public string UnknownProductMember { get; init; }

    /// <summary>Diagnostic ID for an invalid configured equality comparer.</summary>
    public string InvalidComparisonStrategy { get; init; }
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
        string MergeStrategyFieldPrefix,
        string? RebasePolicyFieldPrefix = null
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
        this.RebasePolicyFieldPrefix = RebasePolicyFieldPrefix;
    }

    public string Namespace { get; init; }

    public string OptionalType { get; init; }

    public string MergeStrategyType { get; init; }

    public string ReferenceComparer { get; init; }

    public string ValueComparer { get; init; }

    public string CollectionMerger { get; init; }

    public string CollectionRebase { get; init; }

    public string MergeStrategyFieldPrefix { get; init; }

    /// <summary>Generated per-member rebase policy field prefix, or null for the shared default.</summary>
    public string? RebasePolicyFieldPrefix { get; init; }
}

internal sealed record SparseEditSessionDialect
{
    public SparseEditSessionDialect(
        string Namespace,
        string CoreHintName = "Generated.EditSessionCore.g.cs",
        string CurrentCoreHintName = "Generated.EditSessionWithCurrentCore.g.cs"
    )
    {
        this.Namespace = Namespace;
        this.CoreHintName = CoreHintName;
        this.CurrentCoreHintName = CurrentCoreHintName;
    }

    /// <summary>Namespace containing the generated edit-session helper types.</summary>
    public string Namespace { get; init; }

    /// <summary>Hint name for the generated edit-session helper source.</summary>
    public string CoreHintName { get; init; }

    /// <summary>Hint name for the generated current-view edit-session helper source.</summary>
    public string CurrentCoreHintName { get; init; }
}

internal sealed record SparseDescriptorDialect
{
    public SparseDescriptorDialect(
        string DescriptorInterface,
        string DescriptorSetInterface,
        string ArrayDescriptorInterface,
        string DictionaryDescriptorInterface,
        string DescriptorType,
        string DescriptorSetType,
        string ArrayDescriptorType,
        string ArrayDescriptorAccessType,
        string DictionaryDescriptorType,
        string DictionaryDescriptorAccessType,
        string DescriptorValueType
    )
    {
        this.DescriptorInterface = DescriptorInterface;
        this.DescriptorSetInterface = DescriptorSetInterface;
        this.ArrayDescriptorInterface = ArrayDescriptorInterface;
        this.DictionaryDescriptorInterface = DictionaryDescriptorInterface;
        this.DescriptorType = DescriptorType;
        this.DescriptorSetType = DescriptorSetType;
        this.ArrayDescriptorType = ArrayDescriptorType;
        this.ArrayDescriptorAccessType = ArrayDescriptorAccessType;
        this.DictionaryDescriptorType = DictionaryDescriptorType;
        this.DictionaryDescriptorAccessType = DictionaryDescriptorAccessType;
        this.DescriptorValueType = DescriptorValueType;
    }

    public string DescriptorInterface { get; init; }

    public string DescriptorSetInterface { get; init; }

    public string ArrayDescriptorInterface { get; init; }

    public string DictionaryDescriptorInterface { get; init; }

    public string DescriptorType { get; init; }

    public string DescriptorSetType { get; init; }

    public string ArrayDescriptorType { get; init; }

    public string ArrayDescriptorAccessType { get; init; }

    public string DictionaryDescriptorType { get; init; }

    public string DictionaryDescriptorAccessType { get; init; }

    public string DescriptorValueType { get; init; }
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
