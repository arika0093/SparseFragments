namespace SparseTypes.EntityFrameworkCore;

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

    /// <summary>A changed DTO path addresses a collection member with no keyed synchronization support (declared-only marker, unkeyed sequence, or skip navigation).</summary>
    CollectionNotSupported = 5,

    /// <summary>A changed-to-missing after-state has no handling policy.</summary>
    MissingNotHandled = 6,

    /// <summary>A null value cannot be written to the entity member.</summary>
    NullNotAllowed = 7,

    /// <summary>The mapping contradicts the DTO shape or the EF model.</summary>
    MetadataMismatch = 8,

    /// <summary>A required nested entity instance is missing and cannot be created.</summary>
    NestedNotCreatable = 9,

    /// <summary>A changed collection path addresses a navigation that is not loaded, so sync-by-key has no complete state.</summary>
    CollectionNotLoaded = 10,

    /// <summary>A keyed collection change references a child key with no matching tracked identity in the addressed parent collection.</summary>
    CollectionKeyNotFound = 11,

    /// <summary>A keyed collection change carries colliding, duplicated, or otherwise unresolvable child identities.</summary>
    CollectionKeyConflict = 12,
}

/// <summary>
/// Describes one independently detected mapping validation error.
/// </summary>
public sealed record SparseEntityMappingError
{
    /// <summary>Creates one mapping validation error.</summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="dtoPath">The dotted DTO path that failed, when applicable.</param>
    /// <param name="message">The human-readable detail.</param>
    /// <param name="entityTarget">The entity-side target, when known.</param>
    /// <param name="suggestedAction">A suggested way to correct the mapping.</param>
    public SparseEntityMappingError(
        SparseEntityMappingFailure failure,
        string? dtoPath,
        string message,
        string? entityTarget = null,
        string? suggestedAction = null
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        Failure = failure;
        DtoPath = dtoPath;
        Message = message;
        EntityTarget = entityTarget;
        SuggestedAction = suggestedAction ?? SuggestAction(failure);
    }

    /// <summary>Gets the failure classification.</summary>
    public SparseEntityMappingFailure Failure { get; init; }

    /// <summary>Gets the dotted DTO path that failed, when applicable.</summary>
    public string? DtoPath { get; init; }

    /// <summary>Gets the entity-side target, when known.</summary>
    public string? EntityTarget { get; init; }

    /// <summary>Gets the suggested way to correct the mapping.</summary>
    public string SuggestedAction { get; init; }

    /// <summary>Gets the human-readable detail.</summary>
    public string Message { get; init; }

    private static string SuggestAction(SparseEntityMappingFailure failure) =>
        failure switch
        {
            SparseEntityMappingFailure.Duplicate => "Remove the duplicate registration.",
            SparseEntityMappingFailure.Unmapped => "Map the DTO member explicitly or ignore it.",
            SparseEntityMappingFailure.Ambiguous =>
                "Map each entity member from only one DTO path.",
            SparseEntityMappingFailure.Protected =>
                "Use AllowWrite only when updating this protected member is intentional.",
            SparseEntityMappingFailure.CollectionNotSupported =>
                "Configure keyed collection synchronization or ignore the member.",
            SparseEntityMappingFailure.MissingNotHandled =>
                "Configure an OnMissing policy for the member.",
            SparseEntityMappingFailure.NullNotAllowed =>
                "Map a non-null value or make the entity member nullable.",
            SparseEntityMappingFailure.NestedNotCreatable =>
                "Provide a factory or configure the navigation so the child can be created.",
            SparseEntityMappingFailure.CollectionNotLoaded =>
                "Load the mapped collection before applying its changes.",
            SparseEntityMappingFailure.CollectionKeyNotFound =>
                "Refresh the tracked collection or correct the child identity.",
            SparseEntityMappingFailure.CollectionKeyConflict =>
                "Use unique, resolvable child identities within each collection.",
            SparseEntityMappingFailure.MetadataMismatch =>
                "Align the DTO member, mapping, and EF metadata.",
            _ => "Review the mapping failure detail and correct its cause.",
        };
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
    private readonly IReadOnlyList<SparseEntityMappingError> _errors;

    /// <summary>Creates a mapping failure without a DTO path.</summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="message">The human-readable detail.</param>
    public SparseEntityMappingException(SparseEntityMappingFailure failure, string message)
        : this(failure, null, message) { }

    /// <summary>Creates a mapping failure for a DTO path.</summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="dtoPath">The dotted DTO path that failed.</param>
    /// <param name="message">The human-readable detail.</param>
    /// <param name="entityTarget">The entity-side target, when known.</param>
    /// <param name="suggestedAction">A suggested way to correct the mapping.</param>
    public SparseEntityMappingException(
        SparseEntityMappingFailure failure,
        string? dtoPath,
        string message,
        string? entityTarget = null,
        string? suggestedAction = null
    )
        : base(message)
    {
        Failure = failure;
        DtoPath = dtoPath;
        _errors = Array.AsReadOnly(
            new[]
            {
                new SparseEntityMappingError(
                    failure,
                    dtoPath,
                    message,
                    entityTarget,
                    suggestedAction
                ),
            }
        );
    }

    /// <summary>Creates a mapping failure scoped to one mapper type.</summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="dtoPath">The dotted DTO path that failed.</param>
    /// <param name="message">The human-readable detail.</param>
    /// <param name="mapper">The mapper type that failed.</param>
    /// <param name="inner">The underlying mapping failure.</param>
    internal SparseEntityMappingException(
        SparseEntityMappingFailure failure,
        string? dtoPath,
        string message,
        string mapper,
        Exception inner
    )
        : base(message, inner)
    {
        Failure = failure;
        DtoPath = dtoPath;
        Mapper = mapper;
        _errors = inner is SparseEntityMappingException mappingException
            ? mappingException.Errors
            : Array.AsReadOnly(new[] { new SparseEntityMappingError(failure, dtoPath, message) });
    }

    private SparseEntityMappingException(IReadOnlyList<SparseEntityMappingError> errors)
        : base(CreateAggregateMessage(errors))
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one mapping error is required.", nameof(errors));
        }

        _errors = Array.AsReadOnly(errors.ToArray());
        Failure = errors[0].Failure;
        DtoPath = errors[0].DtoPath;
    }

    /// <summary>Gets the failure classification.</summary>
    public SparseEntityMappingFailure Failure { get; }

    /// <summary>Gets the dotted DTO path that failed, when applicable.</summary>
    public string? DtoPath { get; }

    /// <summary>Gets the mapper type that failed, when scoped by the entry points.</summary>
    public string? Mapper { get; }

    /// <summary>Gets all independently detected mapping validation errors.</summary>
    public IReadOnlyList<SparseEntityMappingError> Errors => _errors;

    internal static SparseEntityMappingException FromErrors(
        IReadOnlyList<SparseEntityMappingError> errors
    )
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 1)
        {
            var error = errors[0];
            return new SparseEntityMappingException(
                error.Failure,
                error.DtoPath,
                error.Message,
                error.EntityTarget,
                error.SuggestedAction
            );
        }

        return new SparseEntityMappingException(errors);
    }

    private static string CreateAggregateMessage(IReadOnlyList<SparseEntityMappingError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one mapping error is required.", nameof(errors));
        }

        if (errors.Count == 1)
        {
            return errors[0].Message;
        }

        var message = new System.Text.StringBuilder()
            .Append(errors.Count)
            .Append(" independent entity mapping validation errors were found:");
        for (var index = 0; index < errors.Count; index++)
        {
            var error = errors[index];
            message.AppendLine().Append(index + 1).Append(". ");
            if (!string.IsNullOrEmpty(error.DtoPath))
            {
                message.Append(error.DtoPath).Append(": ");
            }

            message.Append(error.Failure).Append(": ");
            message.Append(error.Message);
            if (!string.IsNullOrEmpty(error.EntityTarget))
            {
                message.Append(" Entity target: ").Append(error.EntityTarget).Append('.');
            }

            message.Append(" Suggested action: ").Append(error.SuggestedAction);
        }

        return message.ToString();
    }
}
