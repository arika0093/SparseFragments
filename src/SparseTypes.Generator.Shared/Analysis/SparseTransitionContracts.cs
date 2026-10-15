namespace SparseTypes.Generator.Shared;

/// <summary>Product-owned type names for the read-only ChangeSet transition contracts.</summary>
/// <remarks>Shared emission references these contracts only through this configuration.</remarks>
internal sealed record SparseTransitionContracts(
    string ValueChangeInterfaceType,
    string ChangeSetInterfaceType,
    string KeyedChangeInterfaceType,
    string KeyedItemChangeInterfaceType,
    string TemporaryKeyedChangeInterfaceType,
    string DictionaryChangeInterfaceType,
    string DictionaryItemChangeInterfaceType,
    string StructuralDictionaryItemChangeInterfaceType,
    string ChangeRecordType,
    string ChangeKindType
)
{
    /// <summary>Read-only scalar transition contract (open generic, arity 1).</summary>
    public string ValueChangeInterfaceType { get; init; } = ValueChangeInterfaceType;

    /// <summary>Read-only root/nested change-set contract.</summary>
    public string ChangeSetInterfaceType { get; init; } = ChangeSetInterfaceType;

    /// <summary>Optional model-typed root/nested change-set contract.</summary>
    public string? TypedChangeSetInterfaceType { get; init; }

    /// <summary>Read-only keyed-sequence transition contract (open generic, arity 3).</summary>
    public string KeyedChangeInterfaceType { get; init; } = KeyedChangeInterfaceType;

    /// <summary>Single keyed item change contract (open generic, arity 3).</summary>
    public string KeyedItemChangeInterfaceType { get; init; } = KeyedItemChangeInterfaceType;

    /// <summary>Temporary-identity keyed transition contract (open generic, arity 3).</summary>
    public string TemporaryKeyedChangeInterfaceType { get; init; } =
        TemporaryKeyedChangeInterfaceType;

    /// <summary>Read-only dictionary transition contract (open generic, arity 3).</summary>
    public string DictionaryChangeInterfaceType { get; init; } = DictionaryChangeInterfaceType;

    /// <summary>Single dictionary entry change contract (open generic, arity 2).</summary>
    public string DictionaryItemChangeInterfaceType { get; init; } =
        DictionaryItemChangeInterfaceType;

    /// <summary>Structural dictionary entry change contract (open generic, arity 3).</summary>
    public string StructuralDictionaryItemChangeInterfaceType { get; init; } =
        StructuralDictionaryItemChangeInterfaceType;

    /// <summary>Model-independent flattened change record type.</summary>
    public string ChangeRecordType { get; init; } = ChangeRecordType;

    /// <summary>Model-independent change-kind enum type.</summary>
    public string ChangeKindType { get; init; } = ChangeKindType;
}
