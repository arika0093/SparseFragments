using System.ComponentModel;

namespace SparseFragments;

/// <summary>Serializer-agnostic ChangePayload version-1 wire tokens.</summary>
/// <remarks>
/// The base runtime carries no <c>System.Text.Json</c> reference (issue #242).
/// These constants own the v1 contract — envelope field names, endpoint state
/// tokens, item kind tokens, and the polymorphic discriminator — so generated
/// JSON bridges and hand-written codecs map identical strings without
/// duplicating literals. JSON integration itself lives in optional generated
/// serialization artifacts that are only emitted when the consuming
/// compilation references a compatible <c>System.Text.Json</c> API set.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ChangePayloadWire
{
    /// <summary>The current wire version token.</summary>
    public const string Version = "0.1";

    /// <summary>The envelope version property name.</summary>
    public const string VersionProperty = "version";

    /// <summary>The envelope changes property name.</summary>
    public const string ChangesProperty = "changes";

    /// <summary>The polymorphic discriminator property name.</summary>
    public const string DiscriminatorProperty = "member";

    /// <summary>The whole-root discriminator value.</summary>
    public const string RootDiscriminator = "$root";

    /// <summary>The endpoint state property name.</summary>
    public const string StateProperty = "state";

    /// <summary>The endpoint value property name.</summary>
    public const string ValueProperty = "value";

    /// <summary>The <c>missing</c> endpoint state token.</summary>
    public const string MissingState = "missing";

    /// <summary>The <c>null</c> endpoint state token.</summary>
    public const string NullState = "null";

    /// <summary>The <c>value</c> endpoint state token.</summary>
    public const string ValueState = "value";

    /// <summary>The <c>redacted</c> endpoint state token.</summary>
    public const string RedactedState = "redacted";

    /// <summary>The <c>add</c> item kind token.</summary>
    public const string AddKind = "add";

    /// <summary>The <c>remove</c> item kind token.</summary>
    public const string RemoveKind = "remove";

    /// <summary>The <c>edit</c> item kind token.</summary>
    public const string EditKind = "edit";

    /// <summary>The <c>reorder</c> item kind token.</summary>
    public const string ReorderKind = "reorder";

    /// <summary>Maps an endpoint state to its wire token.</summary>
    /// <param name="state">The state to map.</param>
    /// <returns>The lower-case wire token.</returns>
    public static string ToToken(ChangePayloadState state) =>
        state switch
        {
            ChangePayloadState.Missing => MissingState,
            ChangePayloadState.Null => NullState,
            ChangePayloadState.Value => ValueState,
            ChangePayloadState.Redacted => RedactedState,
            _ => throw new System.ArgumentOutOfRangeException(nameof(state), state, null),
        };

    /// <summary>Maps an item kind to its wire token.</summary>
    /// <param name="kind">The kind to map.</param>
    /// <returns>The lower-case wire token.</returns>
    public static string ToToken(ChangePayloadItemKind kind) =>
        kind switch
        {
            ChangePayloadItemKind.Add => AddKind,
            ChangePayloadItemKind.Remove => RemoveKind,
            ChangePayloadItemKind.Edit => EditKind,
            ChangePayloadItemKind.Reorder => ReorderKind,
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>Parses a wire token into an endpoint state.</summary>
    /// <param name="token">The lower-case token, or null.</param>
    /// <param name="state">The parsed state on success.</param>
    /// <returns>Whether the token is a known state.</returns>
    public static bool TryParseState(string? token, out ChangePayloadState state)
    {
        switch (token)
        {
            case MissingState:
                state = ChangePayloadState.Missing;
                return true;
            case NullState:
                state = ChangePayloadState.Null;
                return true;
            case ValueState:
                state = ChangePayloadState.Value;
                return true;
            case RedactedState:
                state = ChangePayloadState.Redacted;
                return true;
            default:
                state = default;
                return false;
        }
    }

    /// <summary>Parses a wire token into an item kind.</summary>
    /// <param name="token">The lower-case token, or null.</param>
    /// <param name="kind">The parsed kind on success.</param>
    /// <returns>Whether the token is a known kind.</returns>
    public static bool TryParseKind(string? token, out ChangePayloadItemKind kind)
    {
        switch (token)
        {
            case AddKind:
                kind = ChangePayloadItemKind.Add;
                return true;
            case RemoveKind:
                kind = ChangePayloadItemKind.Remove;
                return true;
            case EditKind:
                kind = ChangePayloadItemKind.Edit;
                return true;
            case ReorderKind:
                kind = ChangePayloadItemKind.Reorder;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
