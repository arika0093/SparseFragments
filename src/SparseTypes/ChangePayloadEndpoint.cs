using System.ComponentModel;

namespace SparseTypes;

/// <summary>Presence-aware typed endpoint used by generated ChangePayloads.</summary>
/// <remarks>
/// This type is serializer-agnostic: it carries no JSON attributes (issue
/// #242). The v1 wire mapping (<c>state</c>/<c>value</c> names, order, and the
/// lower-case tokens in <see cref="ChangePayloadWire"/>) is implemented by the
/// optional generated JSON bridge, which is only emitted when the consuming
/// compilation references a compatible <c>System.Text.Json</c> API set.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class ChangePayloadEndpoint<T>
{
    /// <summary>Gets or sets the endpoint state.</summary>
    public ChangePayloadState State { get; set; }

    /// <summary>Gets or sets the endpoint value when <see cref="State"/> is <see cref="ChangePayloadState.Value"/>.</summary>
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
    public bool IsRedacted => State == ChangePayloadState.Redacted;

    /// <summary>Validates that value-free states carry no value.</summary>
    /// <remarks>
    /// A redacted before-state is contractually value-free (issue #164): it
    /// marks an undisclosed value, never a confidential value in disguise.
    /// Producers must emit value-free redactions; consumers reject malformed
    /// envelopes before mixed interpretation so secrets cannot hide in a
    /// field the partition bypasses. The rejection never embeds plaintext.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a <c>Missing</c>, <c>Null</c>, or <c>Redacted</c> endpoint
    /// carries a value.
    /// </exception>
    public void Validate()
    {
        // Default(T) is the value-free sentinel: null for reference types and
        // zero for value types, matching what Redacted() and deserialization
        // of a value-free envelope produce.
        if (
            Value is not null
            && !System.Collections.Generic.EqualityComparer<T>.Default.Equals(Value, default!)
        )
        {
            switch (State)
            {
                case ChangePayloadState.Missing:
                    throw new InvalidOperationException(
                        "A missing payload endpoint must not contain a value."
                    );
                case ChangePayloadState.Null:
                    throw new InvalidOperationException(
                        "A null payload endpoint must not contain a value."
                    );
                case ChangePayloadState.Redacted:
                    throw new InvalidOperationException(
                        "A redacted payload endpoint must not contain a value."
                    );
                default:
                    break;
            }
        }
    }

    /// <summary>Converts this endpoint to an optional value.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the endpoint is redacted. A redacted before-state has no known
    /// value to restore, so it cannot form a baseline-aware transition.
    /// </exception>
    public Optional<T> ToOptional()
    {
        Validate();
        if (State == ChangePayloadState.Redacted)
            throw new InvalidOperationException(
                "A redacted payload endpoint has no known value. Project it through a baseline-free patch instead of converting it to an optional value."
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
/// <remarks>
/// Wire tokens are defined by <see cref="ChangePayloadWire"/> and written by
/// the generated JSON bridge; the enum itself carries no serializer
/// attributes so the base runtime stays serializer-agnostic.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum ChangePayloadState
{
    /// <summary>The endpoint is missing.</summary>
    Missing,

    /// <summary>The endpoint is explicitly null.</summary>
    Null,

    /// <summary>The endpoint carries a value.</summary>
    Value,

    /// <summary>The endpoint state is deliberately undisclosed.</summary>
    /// <remarks>
    /// Redacted marks an unavailable before-state (issue #119). It does not mean
    /// <see cref="Missing"/> and does not delete anything: a redacted before-state
    /// paired with a concrete after-state is an explicit write-only operation.
    /// After-states must stay concrete; a redacted after-state is malformed.
    /// </remarks>
    Redacted,
}

/// <summary>The operation represented by a keyed or dictionary payload item.</summary>
/// <remarks>
/// Wire tokens are defined by <see cref="ChangePayloadWire"/> and written by
/// the generated JSON bridge; the enum itself carries no serializer
/// attributes so the base runtime stays serializer-agnostic.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum ChangePayloadItemKind
{
    /// <summary>An item was added.</summary>
    Add,

    /// <summary>An item was removed.</summary>
    Remove,

    /// <summary>An item was edited.</summary>
    Edit,

    /// <summary>An item was reordered.</summary>
    Reorder,
}
