namespace SparseFragments.Generator.Shared;

internal static class SparseWellKnownNames
{
    public const string RuntimeNamespace = "global::SparseFragments";
    public const string RuntimeFacadeNamespace = "global::SparseFragments.CompilerServices";
    public const string OptionalType = RuntimeNamespace + ".Optional";
    public const string MergeStrategyType = RuntimeNamespace + ".FragmentMergeStrategy";
    public const string ValueComparerType = RuntimeFacadeNamespace + ".SparseFragmentRuntime";
    public const string ReferenceComparerType = RuntimeFacadeNamespace + ".SparseFragmentRuntime";
    public const string CollectionMergerType = RuntimeFacadeNamespace + ".SparseFragmentRuntime";
    public const string CollectionRebaseType = RuntimeFacadeNamespace + ".SparseFragmentRuntime";
    public const string JsonPatchBridgeType = RuntimeFacadeNamespace + ".SparseJsonPatchBridge";

    public const string InterfaceSetTypeDefinition = "System.Collections.Generic.ISet<T>";
    public const string ReadOnlySetTypeDefinition = "System.Collections.Generic.IReadOnlySet<T>";

    public const string FragmentTypeName = "Fragment";
    public const string HintNameSuffix = ".SparseFragments.g.cs";
    public const string StructuralHostPrefix = "__SparseStructural_";
    public const string CloneHelperPrefix = "__Clone_";
    public const string MergeStrategyFieldPrefix = "__sparse_merge_strategy_";
}

internal static class SparseDiagnosticIds
{
    public const string MustBePartial = "SPF001";
    public const string UnsupportedModel = "SPF002";
    public const string MissingConstructor = "SPF003";
    public const string InvalidMergeStrategy = "SPF004";
    public const string UnsupportedMerge = "SPF005";
    public const string UnsupportedRequired = "SPF006";
    public const string UnsupportedStructural = "SPF007";
    public const string UnsupportedClone = "SPF008";
    public const string GeneratedNameCollision = "SPF009";
    public const string IncompatiblePromotedModel = "SPF010";
    public const string UnkeyedStructuralSequence = "SPF011";
    public const string ConflictingKeyMechanisms = "SPF012";
    public const string MultiplePropertyKeys = "SPF013";
    public const string InvalidKeyAttributeShape = "SPF014";
    public const string MissingKeyComponent = "SPF015";
    public const string DuplicateKeyComponent = "SPF016";
    public const string InaccessibleKeyProperty = "SPF017";
    public const string NullableKey = "SPF018";
    public const string UnsupportedKeyShape = "SPF019";
    public const string InvalidKeyedInterface = "SPF020";
}
