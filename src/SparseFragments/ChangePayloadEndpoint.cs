using System.ComponentModel;

namespace SparseFragments;

/// <summary>Presence-aware typed endpoint used by generated ChangePayloads.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class ChangePayloadEndpoint<T>
{
    /// <summary>Gets or sets the endpoint state.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    [System.Text.Json.Serialization.JsonPropertyOrder(0)]
    public ChangePayloadState State { get; set; }

    /// <summary>Gets or sets the endpoint value when <see cref="State"/> is <see cref="ChangePayloadState.Value"/>.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("value")]
    [System.Text.Json.Serialization.JsonPropertyOrder(1)]
    public T? Value { get; set; }

    /// <summary>Creates a payload endpoint from an optional value.</summary>
    public static ChangePayloadEndpoint<T> FromOptional(Optional<T> value)
    {
        if (!value.IsPresent)
            return new ChangePayloadEndpoint<T> { State = ChangePayloadState.Missing };
        if (value.Value is null)
            return new ChangePayloadEndpoint<T> { State = ChangePayloadState.Null };
        return new ChangePayloadEndpoint<T>
        {
            State = ChangePayloadState.Value,
            Value = value.Value,
        };
    }

    /// <summary>Creates a redacted endpoint that carries no observable value.</summary>
    /// <remarks>
    /// A redacted endpoint marks a before-state the sender could not disclose
    /// (issue #119). It carries no value by construction and never converts to
    /// <see cref="Optional{T}"/>; project it through a baseline-free patch instead.
    /// Redacted never converts to <see cref="Optional{T}.Missing"/>.
    /// </remarks>
    public static ChangePayloadEndpoint<T> Redacted() =>
        new() { State = ChangePayloadState.Redacted };

    /// <summary>Whether this endpoint redacts its value.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsRedacted => State == ChangePayloadState.Redacted;

    /// <summary>Converts this endpoint to an optional value.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the endpoint is redacted. A redacted before-state has no known
    /// value to restore, so it cannot form a baseline-aware transition.
    /// </exception>
    public Optional<T> ToOptional()
    {
        if (State == ChangePayloadState.Redacted)
            throw new InvalidOperationException(
                "A redacted payload endpoint has no known value. Project it through a baseline-free patch instead of converting it to an optional value."
            );
        if (State == ChangePayloadState.Missing && Value is not null)
            throw new InvalidOperationException(
                "A missing payload endpoint must not contain a value."
            );
        if (State == ChangePayloadState.Null && Value is not null)
            throw new InvalidOperationException(
                "A null payload endpoint must not contain a value."
            );
        if (State == ChangePayloadState.Redacted && Value is not null)
            throw new InvalidOperationException(
                "A redacted payload endpoint must not contain a value."
            );
        return State switch
        {
            ChangePayloadState.Missing => Optional<T>.Missing,
            ChangePayloadState.Null when default(T) is null => Optional<T>.Present(default),
            ChangePayloadState.Null => throw new InvalidOperationException(
                "A non-nullable payload value cannot have a null endpoint."
            ),
            ChangePayloadState.Value when Value is not null => Optional<T>.Present(Value),
            ChangePayloadState.Value => throw new InvalidOperationException(
                "A value endpoint must contain a value."
            ),
            // A redacted before-state is undisclosed, not absent: callers must
            // project through ChangePayload.ToPatch rather than read a baseline.
            _ => throw new InvalidOperationException(
                "A redacted payload endpoint has no observable optional value."
            ),
        };
    }
}

/// <summary>The presence state of a generated ChangePayload endpoint.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
[System.Text.Json.Serialization.JsonConverter(
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ChangePayloadState>)
)]
public enum ChangePayloadState
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

    /// <summary>The endpoint state is deliberately undisclosed.</summary>
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
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ChangePayloadItemKind>)
)]
public enum ChangePayloadItemKind
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
