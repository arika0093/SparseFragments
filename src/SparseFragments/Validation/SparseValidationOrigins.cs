using System.ComponentModel;

namespace SparseFragments;

/// <summary>Well-known error origins for <see cref="SparseValidationStore{TModel}"/>.</summary>
/// <remarks>
/// Origins keep validators independent: replacing or clearing one origin never
/// touches errors recorded under another. Applications may use any non-empty
/// custom origin string beyond these three; origins are opaque and compared
/// ordinally.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseValidationOrigins
{
    /// <summary>Gets the origin for client-side validation results.</summary>
    /// <remarks>
    /// Lives until the next explicit validation pass replaces or clears it;
    /// never refreshed implicitly on keystroke.
    /// </remarks>
    public const string LocalValidation = "local";

    /// <summary>Gets the origin for server-round-trip validation results.</summary>
    /// <remarks>Lives until the next submit round-trip replaces or clears it.</remarks>
    public const string ServerValidation = "server";

    /// <summary>Gets the origin for rebase-conflict errors.</summary>
    /// <remarks>
    /// Lives until the application accepts, reconciles, or reloads past the
    /// conflict; mapping a conflict never resolves it.
    /// </remarks>
    public const string RebaseConflict = "conflict";
}
