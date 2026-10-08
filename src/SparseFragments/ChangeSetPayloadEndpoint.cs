using System.ComponentModel;

namespace SparseFragments;

/// <summary>Presence-aware typed endpoint used by generated ChangeSet payloads.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class ChangeSetPayloadEndpoint<T>
{
    /// <summary>Gets or sets the endpoint state.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    [System.Text.Json.Serialization.JsonPropertyOrder(0)]
    public ChangeSetPayloadState State { get; set; }

    /// <summary>Gets or sets the endpoint value when <see cref="State"/> is <see cref="ChangeSetPayloadState.Value"/>.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("value")]
    [System.Text.Json.Serialization.JsonPropertyOrder(1)]
    public T? Value { get; set; }

    /// <summary>Creates a payload endpoint from an optional value.</summary>
    public static ChangeSetPayloadEndpoint<T> FromOptional(Optional<T> value)
    {
        if (!value.IsPresent)
            return new ChangeSetPayloadEndpoint<T> { State = ChangeSetPayloadState.Missing };
        if (value.Value is null)
            return new ChangeSetPayloadEndpoint<T> { State = ChangeSetPayloadState.Null };
        return new ChangeSetPayloadEndpoint<T>
        {
            State = ChangeSetPayloadState.Value,
            Value = value.Value,
        };
    }

    /// <summary>Creates a redacted endpoint with no usable value.</summary>
    /// <remarks>
    /// A redacted endpoint marks a before-state the sender could not disclose
    /// (issue #119). It carries no value by construction and never converts to
    /// <see cref="Optional{T}"/>; project it through a baseline-free patch instead.
    /// </remarks>
    public static ChangeSetPayloadEndpoint<T> Redacted() =>
        new() { State = ChangeSetPayloadState.Redacted };

    /// <summary>Whether this endpoint redacts its value.</summary>
    public bool IsRedacted => State == ChangeSetPayloadState.Redacted;

    /// <summary>Converts this endpoint to an optional value.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the endpoint is redacted. A redacted before-state has no known
    /// value to restore, so it cannot form a baseline-aware transition.
    /// </exception>
    public Optional<T> ToOptional()
    {
        if (State == ChangeSetPayloadState.Redacted)
            throw new InvalidOperationException(
                "A redacted payload endpoint has no known value. Project it through a baseline-free patch instead of converting it to an optional value."
            );
        if (State == ChangeSetPayloadState.Missing && Value is not null)
            throw new InvalidOperationException(
                "A missing payload endpoint must not contain a value."
            );
        if (State == ChangeSetPayloadState.Null && Value is not null)
            throw new InvalidOperationException(
                "A null payload endpoint must not contain a value."
            );
        return State switch
        {
            ChangeSetPayloadState.Missing => Optional<T>.Missing,
            ChangeSetPayloadState.Null when default(T) is null => Optional<T>.Present(default),
            ChangeSetPayloadState.Null => throw new InvalidOperationException(
                "A non-nullable payload value cannot have a null endpoint."
            ),
            ChangeSetPayloadState.Value when Value is not null => Optional<T>.Present(Value),
            ChangeSetPayloadState.Value => throw new InvalidOperationException(
                "A value endpoint must contain a value."
            ),
            _ => throw new InvalidOperationException("The payload endpoint state is invalid."),
        };
    }
}

/// <summary>The presence state of a generated ChangeSet payload endpoint.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
[System.Text.Json.Serialization.JsonConverter(
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ChangeSetPayloadState>)
)]
public enum ChangeSetPayloadState
{
    /// <summary>The endpoint is missing.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("missing")]
    Missing,

    /// <summary>The endpoint is explicitly null.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("null")]
    Null,

    /// <summary>The endpoint carries a value.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("value")]
    Value,

    /// <summary>The endpoint value was redacted by the sender.</summary>
    /// <remarks>
    /// Redacted marks an unavailable before-state (issue #119). It does not mean
    /// <see cref="Missing"/> and does not delete anything: a redacted before-state
    /// paired with a concrete after-state is an explicit write-only operation.
    /// After-states must stay concrete; a redacted after-state is malformed.
    /// </remarks>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("redacted")]
    Redacted,
}

/// <summary>The operation represented by a keyed or dictionary payload item.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
[System.Text.Json.Serialization.JsonConverter(
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ChangeSetPayloadItemKind>)
)]
public enum ChangeSetPayloadItemKind
{
    /// <summary>An item was added.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("add")]
    Add,

    /// <summary>An item was removed.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("remove")]
    Remove,

    /// <summary>An item was edited.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("edit")]
    Edit,

    /// <summary>An item was reordered.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("reorder")]
    Reorder,
}
