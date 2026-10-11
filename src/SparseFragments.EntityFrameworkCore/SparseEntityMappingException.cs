namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Classifies why an entity mapping plan or apply step failed.
/// </summary>
public enum SparseEntityMappingFailure
{
    /// <summary>An unclassified mapping failure.</summary>
    Unknown = 0,

    /// <summary>The same DTO path was registered twice.</summary>
    Duplicate = 1,

    /// <summary>A changed DTO path has no mapping and is not ignored.</summary>
    Unmapped = 2,

    /// <summary>Two DTO paths target the same entity member.</summary>
    Ambiguous = 3,

    /// <summary>A changed DTO path targets an identity, generated, concurrency-token, or shadow member without explicit allowance.</summary>
    Protected = 4,

    /// <summary>A changed DTO path addresses a collection member, which this proof of concept does not synchronize (see issue #225).</summary>
    CollectionNotSupported = 5,

    /// <summary>A changed-to-missing after-state has no handling policy.</summary>
    MissingNotHandled = 6,

    /// <summary>A null value cannot be written to the entity member.</summary>
    NullNotAllowed = 7,

    /// <summary>The mapping contradicts the DTO shape or the EF model.</summary>
    MetadataMismatch = 8,

    /// <summary>A required nested entity instance is missing and cannot be created.</summary>
    NestedNotCreatable = 9,
}

/// <summary>
/// Reports an explicit entity-mapping plan or apply failure.
/// </summary>
/// <remarks>
/// Unsupported changed paths never disappear silently: unmapped, ambiguous,
/// protected, and collection paths all surface through this exception with the
/// offending DTO path attached.
/// </remarks>
public sealed class SparseEntityMappingException : InvalidOperationException
{
    /// <summary>Creates a mapping failure without a DTO path.</summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="message">The human-readable detail.</param>
    public SparseEntityMappingException(SparseEntityMappingFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>Creates a mapping failure for a DTO path.</summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="dtoPath">The dotted DTO path that failed.</param>
    /// <param name="message">The human-readable detail.</param>
    public SparseEntityMappingException(
        SparseEntityMappingFailure failure,
        string? dtoPath,
        string message
    )
        : base(message)
    {
        Failure = failure;
        DtoPath = dtoPath;
    }

    /// <summary>Gets the failure classification.</summary>
    public SparseEntityMappingFailure Failure { get; }

    /// <summary>Gets the dotted DTO path that failed, when applicable.</summary>
    public string? DtoPath { get; }
}
