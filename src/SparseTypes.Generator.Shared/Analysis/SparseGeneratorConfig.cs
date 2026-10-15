using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

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
        string? EditSessionWithPathsInterfaceMetadataName = null,
        SparseDescriptorDialect? DescriptorDialect = null,
        string? GeneratedImplementationNamespace = null,
        SparseFamilyNames? FamilyNames = null,
        string? GeneratedImplementationSuffix = null,
        string? TemporaryKeyAttributeMetadataName = null,
        bool AllowFileScopedTypes = false,
        SparseStjCapabilities? StjCapabilities = null,
        string? EditChildPathInterfaceMetadataName = null,
        string? EditChildSessionInterfaceMetadataName = null,
        string? EditChildSessionImplementationType = null,
        string? EditSessionWithChildrenInterfaceMetadataName = null,
        string? EditChildSessionStatusType = null,
        string? EditSessionWithChildStatusInterfaceMetadataName = null,
        string? EditSessionWithChangeSetInterfaceMetadataName = null
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
        this.EditSessionWithPathsInterfaceMetadataName = EditSessionWithPathsInterfaceMetadataName;
        this.DescriptorDialect = DescriptorDialect;
        this.GeneratedImplementationNamespace = GeneratedImplementationNamespace;
        this.FamilyNames = FamilyNames;
        this.GeneratedImplementationSuffix = GeneratedImplementationSuffix;
        this.TemporaryKeyAttributeMetadataName = TemporaryKeyAttributeMetadataName;
        this.AllowFileScopedTypes = AllowFileScopedTypes;
        this.StjCapabilities = StjCapabilities;
        this.EditChildPathInterfaceMetadataName = EditChildPathInterfaceMetadataName;
        this.EditChildSessionInterfaceMetadataName = EditChildSessionInterfaceMetadataName;
        this.EditChildSessionImplementationType = EditChildSessionImplementationType;
        this.EditSessionWithChildrenInterfaceMetadataName =
            EditSessionWithChildrenInterfaceMetadataName;
        this.EditChildSessionStatusType = EditChildSessionStatusType;
        this.EditSessionWithChildStatusInterfaceMetadataName =
            EditSessionWithChildStatusInterfaceMetadataName;
        this.EditSessionWithChangeSetInterfaceMetadataName =
            EditSessionWithChangeSetInterfaceMetadataName;
    }

    public string ModelAttributeMetadataName { get; init; }

    public string IgnoreAttributeMetadataName { get; init; }

    public string RedactBeforeAttributeMetadataName { get; init; }

    public string MergeAttributeMetadataName { get; init; }

    public string MergeStrategyBaseMetadataName { get; init; }

    public string CloneReferenceSafeAttributeMetadataName { get; init; }

    public SparseStructuralPolicy StructuralPolicy { get; init; }

    public string KeyAttributeMetadataName { get; init; }

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

    /// <summary>Trusted model-accessor interface implemented by generated model sessions, or null when disabled.</summary>
    public string? EditSessionModelAccessorInterfaceMetadataName { get; init; }

    /// <summary>Typed path-provider interface implemented by generated root sessions, or null when disabled.</summary>
    /// <remarks>Null disables the <c>Paths</c> member for products without this session contract.</remarks>
    public string? EditSessionWithPathsInterfaceMetadataName { get; init; }

    /// <summary>Typed child-path factory interface implemented by generated model paths, or null when disabled.</summary>
    public string? EditChildPathInterfaceMetadataName { get; init; }

    /// <summary>Child edit-session contract returned by generated parent sessions, or null when disabled.</summary>
    public string? EditChildSessionInterfaceMetadataName { get; init; }

    /// <summary>Runtime child-session implementation type, or null when disabled.</summary>
    public string? EditChildSessionImplementationType { get; init; }

    /// <summary>Nested-session coordination contract implemented by generated sessions, or null when disabled.</summary>
    public string? EditSessionWithChildrenInterfaceMetadataName { get; init; }

    /// <summary>Product-owned child-session status enum, or null when child sessions are disabled.</summary>
    public string? EditChildSessionStatusType { get; init; }

    /// <summary>Extended child-session contract with typed state and preflight operations.</summary>
    public string? EditSessionWithChildStatusInterfaceMetadataName { get; init; }

    /// <summary>Optional generic session-and-change-set adapter interface, or null when disabled.</summary>
    public string? EditSessionWithChangeSetInterfaceMetadataName { get; init; }

    /// <summary>Product-owned descriptor contracts and runtime helper names, or null when disabled.</summary>
    public SparseDescriptorDialect? DescriptorDialect { get; init; }

    /// <summary>Explicit per-model implementation namespace, or null for single-file emission.</summary>
    /// <remarks>Stages that relocate generated machinery outside the attributed model require an explicit namespace from the owning generator.</remarks>
    public string? GeneratedImplementationNamespace { get; init; }

    /// <summary>Product-owned generated family names, or null for the standalone vocabulary.</summary>
    /// <remarks>Products may rename shared semantic roles.</remarks>
    public SparseFamilyNames? FamilyNames { get; init; }

    /// <summary>Product-owned implementation hint-name suffix, or null for the shared default.</summary>
    /// <remarks>Relocated implementation hints use this suffix.</remarks>
    public string? GeneratedImplementationSuffix { get; init; }

    /// <summary>Attribute marking the temporary-identity property, or null when the product has none.</summary>
    /// <remarks>
    /// Null disables temporary-key analysis so downstream products without
    /// the attribute keep byte-identical output.
    /// </remarks>
    public string? TemporaryKeyAttributeMetadataName { get; init; }

    /// <summary>Whether the consumer language version allows file-scoped types.</summary>
    /// <remarks>False keeps the legacy <c>internal</c> helpers so downstream products without C# 11 keep byte-identical output.</remarks>
    public bool AllowFileScopedTypes { get; init; }

    /// <summary>System.Text.Json surface available to generated code, or null for the full surface.</summary>
    /// <remarks>Null preserves unconditional JSON emission for products that do not inspect the compilation.</remarks>
    public SparseStjCapabilities? StjCapabilities { get; init; }

    /// <summary>Effective JSON surface, defaulting to the full historical set.</summary>
    public SparseStjCapabilities EffectiveStjCapabilities =>
        StjCapabilities ?? SparseStjCapabilities.All;

    /// <summary>Whether generated JSON artifacts may be emitted for this configuration.</summary>
    /// <remarks>Emission also requires V1 wire-format support.</remarks>
    public bool EffectiveEmitJson =>
        EffectiveEmissionFeatures.EmitJsonConverters && EffectiveStjCapabilities.SupportsV1Wire;

    public SparseMergeModeMap EffectiveMergeModeMap => MergeModeMap;

    public SparseDiagnosticIdMap EffectiveDiagnosticIds => DiagnosticIds;

    public ImmutableArray<string> EffectiveReservedGeneratedNames =>
        ReservedGeneratedNames.IsDefault ? ImmutableArray<string>.Empty : ReservedGeneratedNames;

    /// <summary>Effective feature selection, defaulting to the standalone set.</summary>
    public SparseEmissionFeatures EffectiveEmissionFeatures =>
        EmissionFeatures ?? SparseEmissionFeatures.Standalone;

    /// <summary>Effective family-name bindings, defaulting to the standalone vocabulary.</summary>
    public SparseFamilyNames EffectiveFamilyNames => FamilyNames ?? SparseFamilyNames.Standalone;

    /// <summary>Effective implementation hint-name suffix, defaulting to the shared convention.</summary>
    public string EffectiveGeneratedImplementationSuffix =>
        GeneratedImplementationSuffix is { Length: > 0 }
            ? GeneratedImplementationSuffix
            : SparseGeneratedPlacement.DefaultImplementationSuffix;

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
        string MultiplePropertyKeys,
        string InvalidKeyAttributeShape,
        string InaccessibleKeyProperty,
        string UnsupportedKeyShape,
        string DuplicateJsonPropertyName,
        string SparseIgnoreOnKey,
        string SparseIgnoreUnsupportedProperty,
        string InvalidRebasePolicy = "SPF027",
        string InvalidEmissionPlan = "SPF028",
        string UnknownProductMember = "SPF029",
        string InvalidComparisonStrategy = "SPF030",
        string InvalidTemporaryKeyShape = "SPF031",
        string TemporaryKeyInitializer = "SPF032"
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
        this.MultiplePropertyKeys = MultiplePropertyKeys;
        this.InvalidKeyAttributeShape = InvalidKeyAttributeShape;
        this.InaccessibleKeyProperty = InaccessibleKeyProperty;
        this.UnsupportedKeyShape = UnsupportedKeyShape;
        this.DuplicateJsonPropertyName = DuplicateJsonPropertyName;
        this.SparseIgnoreOnKey = SparseIgnoreOnKey;
        this.SparseIgnoreUnsupportedProperty = SparseIgnoreUnsupportedProperty;
        this.InvalidRebasePolicy = InvalidRebasePolicy;
        this.InvalidEmissionPlan = InvalidEmissionPlan;
        this.UnknownProductMember = UnknownProductMember;
        this.InvalidComparisonStrategy = InvalidComparisonStrategy;
        this.InvalidTemporaryKeyShape = InvalidTemporaryKeyShape;
        this.TemporaryKeyInitializer = TemporaryKeyInitializer;
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

    public string MultiplePropertyKeys { get; init; }

    public string InvalidKeyAttributeShape { get; init; }

    public string InaccessibleKeyProperty { get; init; }

    public string UnsupportedKeyShape { get; init; }

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

    /// <summary>Diagnostic ID for an invalid temporary-identity declaration.</summary>
    public string InvalidTemporaryKeyShape { get; init; }

    /// <summary>Diagnostic ID for an explicit initializer on a temporary-identity property.</summary>
    public string TemporaryKeyInitializer { get; init; }
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

    /// <summary>Optional parameterless factory for a disposable scope exposing a reference-identity Dictionary&lt.</summary>
    public string? CloneContextScopeFactory { get; init; }

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
        string CurrentCoreHintName = "Generated.EditSessionWithCurrentCore.g.cs",
        string? HistoryType = null,
        string? HistoryOptionsType = null,
        string? HistoryHooksType = null,
        string? HistoryContractType = null
    )
    {
        this.Namespace = Namespace;
        this.CoreHintName = CoreHintName;
        this.CurrentCoreHintName = CurrentCoreHintName;
        this.HistoryType = HistoryType;
        this.HistoryOptionsType = HistoryOptionsType;
        this.HistoryHooksType = HistoryHooksType;
        this.HistoryContractType = HistoryContractType;
    }

    /// <summary>Namespace containing the generated edit-session helper types.</summary>
    public string Namespace { get; init; }

    /// <summary>Hint name for the generated edit-session helper source.</summary>
    public string CoreHintName { get; init; }

    /// <summary>Hint name for the generated current-view edit-session helper source.</summary>
    public string CurrentCoreHintName { get; init; }

    /// <summary>Optional opt-in history type, or null when history is not emitted.</summary>
    /// <remarks>Product-owned reusable history core (for example <c>global::SparseTypes.SparseEditHistory</c>).</remarks>
    public string? HistoryType { get; init; }

    /// <summary>Optional history options type, or null when history is not emitted.</summary>
    public string? HistoryOptionsType { get; init; }

    /// <summary>Optional history hooks type, or null when history is not emitted.</summary>
    public string? HistoryHooksType { get; init; }

    /// <summary>Optional non-generic history control contract, or null for the legacy factory only.</summary>
    /// <remarks>Product-owned framework-neutral contract (for example <c>global::SparseTypes.ISparseEditHistory</c>).</remarks>
    public string? HistoryContractType { get; init; }

    /// <summary>Whether the dialect carries a complete history binding.</summary>
    public bool HasHistory =>
        HistoryType is { Length: > 0 }
        && HistoryOptionsType is { Length: > 0 }
        && HistoryHooksType is { Length: > 0 };

    /// <summary>Whether the dialect carries the single-owned history contract.</summary>
    public bool HasOwnedHistory => HasHistory && HistoryContractType is { Length: > 0 };
}

internal sealed record SparseDescriptorDialect
{
    public SparseDescriptorDialect(
        string DescriptorInterface,
        string DescriptorSetInterface,
        string ArrayDescriptorInterface,
        string DictionaryDescriptorInterface,
        string SetDescriptorInterface,
        string DescriptorType,
        string DescriptorSetType,
        string ArrayDescriptorType,
        string ArrayDescriptorAccessType,
        string DictionaryDescriptorType,
        string DictionaryDescriptorAccessType,
        string SetDescriptorType,
        string SetDescriptorAccessType,
        string DescriptorShapeType,
        string DescriptorValueType,
        string? PathType = null
    )
    {
        this.DescriptorInterface = DescriptorInterface;
        this.DescriptorSetInterface = DescriptorSetInterface;
        this.ArrayDescriptorInterface = ArrayDescriptorInterface;
        this.DictionaryDescriptorInterface = DictionaryDescriptorInterface;
        this.SetDescriptorInterface = SetDescriptorInterface;
        this.DescriptorType = DescriptorType;
        this.DescriptorSetType = DescriptorSetType;
        this.ArrayDescriptorType = ArrayDescriptorType;
        this.ArrayDescriptorAccessType = ArrayDescriptorAccessType;
        this.DictionaryDescriptorType = DictionaryDescriptorType;
        this.DictionaryDescriptorAccessType = DictionaryDescriptorAccessType;
        this.SetDescriptorType = SetDescriptorType;
        this.SetDescriptorAccessType = SetDescriptorAccessType;
        this.DescriptorShapeType = DescriptorShapeType;
        this.DescriptorValueType = DescriptorValueType;
        this.PathType = PathType;
    }

    public string DescriptorInterface { get; init; }

    public string DescriptorSetInterface { get; init; }

    public string ArrayDescriptorInterface { get; init; }

    public string DictionaryDescriptorInterface { get; init; }

    public string SetDescriptorInterface { get; init; }

    public string DescriptorType { get; init; }

    public string DescriptorSetType { get; init; }

    public string ArrayDescriptorType { get; init; }

    public string ArrayDescriptorAccessType { get; init; }

    public string DictionaryDescriptorType { get; init; }

    public string DictionaryDescriptorAccessType { get; init; }

    public string SetDescriptorType { get; init; }

    public string SetDescriptorAccessType { get; init; }

    public string DescriptorShapeType { get; init; }

    public string DescriptorValueType { get; init; }

    /// <summary>Product-owned path type for descriptor paths, or null for the runtime-namespace default.</summary>
    public string? PathType { get; init; }

    /// <summary>Resolves the descriptor path type, defaulting to the runtime namespace.</summary>
    /// <param name="runtimeNamespace">Runtime helper namespace.</param>
    public string EffectivePathType(string runtimeNamespace) =>
        PathType ?? runtimeNamespace + "SparsePath";
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
