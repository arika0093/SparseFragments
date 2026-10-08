namespace SparseFragments.Generator.Shared;

internal static class SparseWellKnownNames
{
    public const string InterfaceSetTypeDefinition = "System.Collections.Generic.ISet<T>";
    public const string ReadOnlySetTypeDefinition = "System.Collections.Generic.IReadOnlySet<T>";

    public const string FragmentTypeName = "Fragment";
    public const string CloneHelperPrefix = "__Clone_";
    public const string MergeStrategyFieldPrefix = "__sparse_merge_strategy_";
}
