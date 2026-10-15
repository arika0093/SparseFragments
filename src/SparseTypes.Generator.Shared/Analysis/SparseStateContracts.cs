namespace SparseTypes.Generator.Shared;

/// <summary>Product-owned type names for the semantic field and collection state surface.</summary>
/// <remarks>Shared emission references these state types only through this configuration.</remarks>
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

    /// <summary>Opt-in live field-state subscription type (open generic, arity 1).</summary>
    /// <remarks>
    /// Empty disables the observe surface while keeping the point-in-time
    /// queries byte-identical for products without the watcher type.
    /// </remarks>
    public string ObservedFieldType { get; init; } = string.Empty;

    /// <summary>Watcher hooks type binding a subscription to one session.</summary>
    public string ObservedFieldHooksType { get; init; } = string.Empty;

    /// <summary>Whether the observe surface is bound.</summary>
    public bool HasObservedField =>
        ObservedFieldType.Length != 0 && ObservedFieldHooksType.Length != 0;
}
