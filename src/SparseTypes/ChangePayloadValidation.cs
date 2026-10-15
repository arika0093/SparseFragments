using System.ComponentModel;

namespace SparseTypes;

/// <summary>Structural categories reported by ChangePayload validation.</summary>
/// <remarks>Validation covers the envelope wire contract only. It never reports
/// rebase conflicts, authorization, or domain-rule failures.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum ChangePayloadValidationKind
{
    /// <summary>The envelope version is missing or unsupported.</summary>
    Version,

    /// <summary>The envelope or member structure is malformed.</summary>
    Changes,

    /// <summary>A member, key, or temporary identity appears twice.</summary>
    Duplicate,

    /// <summary>An endpoint state or endpoint combination is illegal.</summary>
    Endpoint,

    /// <summary>A collection item kind, index, or edit core is malformed.</summary>
    Item,

    /// <summary>A key order or temporary order is missing.</summary>
    Order,
}

/// <summary>One structural problem found in a ChangePayload.</summary>
/// <remarks>Messages carry member paths and reasons only, never before, current,
/// or after values.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed record ChangePayloadValidationError
{
    /// <summary>Gets the structural category of this error.</summary>
    public ChangePayloadValidationKind Kind { get; init; }

    /// <summary>Gets the dotted member path, with [key] segments for collection items.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets the safe human-readable reason.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>The outcome of validating a ChangePayload without converting it.</summary>
/// <remarks>Serializer-independent: validation inspects the payload DTOs and needs
/// neither System.Text.Json nor a current model instance.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class ChangePayloadValidationResult
{
    /// <summary>Creates a validation outcome from the collected errors.</summary>
    /// <param name="errors">The structural errors in deterministic payload order.</param>
    public ChangePayloadValidationResult(
        System.Collections.Generic.IReadOnlyList<ChangePayloadValidationError> errors
    )
    {
        Errors = errors;
    }

    /// <summary>Gets the structural errors in deterministic payload order.</summary>
    public System.Collections.Generic.IReadOnlyList<ChangePayloadValidationError> Errors { get; }

    /// <summary>Whether the payload is structurally valid.</summary>
    public bool IsValid => Errors.Count == 0;
}
