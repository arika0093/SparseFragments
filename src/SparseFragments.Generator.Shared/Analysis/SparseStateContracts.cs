namespace SparseFragments.Generator.Shared;

/// <summary>Product-owned type names for the semantic field and collection state surface.</summary>
/// <remarks>
/// Shared emission references these state types only through this configuration:
/// no <c>global::SparseFragments.*</c> literal appears in the emitters. Null
/// disables state emission so products without the runtime state types keep
/// byte-identical output. The state surface additionally requires the
/// transition contracts (typed transitions back the collection view) and the
/// typed-path interface (typed field queries).
/// </remarks>
internal sealed record SparseStateContracts(
    string FieldStateType,
    string CollectionStateType,
    string StateQueryType
)
{
    /// <summary>Semantic field state type (open generic, arity 1).</summary>
    public string FieldStateType { get; init; } = FieldStateType;

    /// <summary>Collection state view type over one keyed or dictionary member.</summary>
    public string CollectionStateType { get; init; } = CollectionStateType;

    /// <summary>Static path-first state query type.</summary>
    public string StateQueryType { get; init; } = StateQueryType;
}
