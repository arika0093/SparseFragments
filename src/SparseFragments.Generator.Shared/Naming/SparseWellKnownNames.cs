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

    public const string InterfaceSetTypeDefinition = "System.Collections.Generic.ISet<T>";
    public const string ReadOnlySetTypeDefinition = "System.Collections.Generic.IReadOnlySet<T>";

    public const string FragmentTypeName = "Fragment";
    public const string HintNameSuffix = ".SparseFragments.g.cs";
    public const string StructuralHostPrefix = "__SparseStructural_";
    public const string CloneHelperPrefix = "__Clone_";
    public const string MergeStrategyFieldPrefix = "__sparse_merge_strategy_";
}
