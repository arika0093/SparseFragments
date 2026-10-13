using System.ComponentModel;

namespace SparseTypes;

/// <summary>Options controlling conflict-to-error projection.</summary>
/// <remarks>
/// Mirrors the message wording and path semantics of the Blazor
/// <c>SparseConflictValidationOptions</c> (issue #255) while staying free of
/// <c>EditContext</c>: Core stores the exact conflict path and surfaces
/// parent views through subtree queries instead of resolving against a live
/// model.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed record SparseValidationConflictOptions
{
    /// <summary>Gets the origin recorded for projected conflicts.</summary>
    public string Origin { get; init; } = SparseValidationOrigins.RebaseConflict;

    /// <summary>Gets whether existing errors under <see cref="Origin"/> are cleared first.</summary>
    /// <remarks>Only that origin is cleared; other origins stay intact.</remarks>
    public bool ReplaceOriginFirst { get; init; }

    /// <summary>Gets the per-conflict message factory, or null for the default message.</summary>
    public Func<SparseConflict, string>? MessageFactory { get; init; }

    /// <summary>Builds the error message for one conflict.</summary>
    /// <param name="conflict">The conflict to describe.</param>
    /// <returns>The message; defaults name the path and kind so conflicts stay distinct from validation messages.</returns>
    public string GetMessage(SparseConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        if (MessageFactory is not null)
        {
            return MessageFactory(conflict);
        }

        var message =
            $"Conflict at '{conflict.PathText}' ({conflict.Kind}): the server kept a newer value.";
        return string.IsNullOrEmpty(conflict.Reason) ? message : $"{message} {conflict.Reason}";
    }
}
