namespace SparseTypes.Generator.Shared;

/// <summary>Product-owned session capability interface names implemented by generated edit sessions.</summary>
/// <remarks>Shared emission only appends these base interfaces; the matching public members already exist. Null or empty disables one capability so downstream products without the contract keep byte-identical output.</remarks>
internal sealed record SparseSessionCapabilityContracts(
    string? HistoryInterface = null,
    string? FieldStateInterface = null,
    string? CollectionStateInterface = null,
    string? BatchEditInterface = null,
    string? DescriptorSessionInterface = null,
    string? StateSnapshotInterface = null
)
{
    /// <summary>Session-owned history contract, or null when history capabilities are disabled.</summary>
    public string? HistoryInterface { get; init; } = HistoryInterface;

    /// <summary>Generic field-state contract over one model type (open generic, arity 1), or null when disabled.</summary>
    public string? FieldStateInterface { get; init; } = FieldStateInterface;

    /// <summary>Collection-state contract, or null when disabled.</summary>
    public string? CollectionStateInterface { get; init; } = CollectionStateInterface;

    /// <summary>Batch-edit contract, or null when disabled.</summary>
    public string? BatchEditInterface { get; init; } = BatchEditInterface;

    /// <summary>Descriptor-session contract, or null when disabled.</summary>
    public string? DescriptorSessionInterface { get; init; } = DescriptorSessionInterface;

    /// <summary>State-snapshot contract, or null when disabled.</summary>
    public string? StateSnapshotInterface { get; init; } = StateSnapshotInterface;

    /// <summary>Whether any session capability contract is bound.</summary>
    public bool HasAny =>
        HistoryInterface is { Length: > 0 }
        || FieldStateInterface is { Length: > 0 }
        || CollectionStateInterface is { Length: > 0 }
        || BatchEditInterface is { Length: > 0 }
        || DescriptorSessionInterface is { Length: > 0 }
        || StateSnapshotInterface is { Length: > 0 };
}
