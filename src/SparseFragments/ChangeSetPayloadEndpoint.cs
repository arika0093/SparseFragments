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

    /// <summary>Converts this endpoint to an optional value.</summary>
    public Optional<T> ToOptional()
    {
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
