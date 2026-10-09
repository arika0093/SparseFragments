using System.ComponentModel;

namespace SparseFragments;

/// <summary>Per-rebase options for generated <c>RebaseOnto</c> and <c>TryApplyTo</c> entry points.</summary>
/// <remarks>
/// This type is the isolated adapter between the typed payload flow and rebase
/// semantics. Redacted (write-only) members are named by dotted path
/// (<c>"Secret"</c>, <c>"Nested.Token"</c>, <c>""</c> for the whole
/// contribution); the wire version stays <c>"0.1"</c> and carries no redacted
/// markers, so callers populate <see cref="RedactedBeforePaths"/> from their
/// own metadata until the payload format learns them.
/// A redacted member is never <see cref="Optional{T}.Missing"/>: the desired
/// value is present, only its before-state is withheld.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed record ChangePayloadRebaseOptions
{
    /// <summary>Whether a redacted-before member fails rebase instead of passing through.</summary>
    /// <remarks>
    /// Defaults to <c>false</c>: the desired operation applies as its explicit
    /// patch operation with no historical comparison, no invented baseline, and
    /// no automatic undo. Set to <c>true</c> to fail with a secret-safe
    /// <see cref="SparseConflictKind.RedactedBefore"/> conflict instead.
    /// </remarks>
    public bool RejectChangesWithRedactedBeforeValuesDuringRebase { get; init; }

    /// <summary>Dotted paths treated as redacted-before for this rebase attempt.</summary>
    /// <remarks>
    /// A path redacts its whole subtree: <c>"Nested"</c> covers
    /// <c>"Nested.Host"</c>. The empty string redacts the whole contribution.
    /// </remarks>
    public IReadOnlyList<string> RedactedBeforePaths { get; init; } = Array.Empty<string>();

    /// <summary>Fallback reconciliation for members without explicit rebase configuration.</summary>
    /// <remarks>
    /// Applies to scalar and whole-replace members only; nested, keyed,
    /// dictionary, and merge-collection members always reconcile granularly.
    /// </remarks>
    public SparseRebaseMode DefaultRebaseMode { get; init; } = SparseRebaseMode.Default;

    /// <summary>Whether the given dotted path is redacted-before under these options.</summary>
    public bool IsRedactedBefore(string? pathText)
    {
        pathText ??= string.Empty;
        return RedactedBeforePaths.Any(entry => IsRedactionMatch(entry, pathText));
    }

    /// <summary>Whether the given member path is redacted-before under these options.</summary>
    public bool IsRedactedBefore(IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return IsRedactedBefore(string.Join(".", path));
    }

    /// <summary>Returns options with paths rebased one level down for a nested member.</summary>
    /// <remarks>
    /// The nested rebase receives root-relative paths stripped of
    /// <paramref name="segment"/>; an exact match becomes whole-subtree
    /// redaction. Other settings pass through unchanged.
    /// </remarks>
    public ChangePayloadRebaseOptions Nest(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (RedactedBeforePaths.Count == 0)
        {
            return this;
        }

        var prefix = segment + ".";
        List<string>? nested = null;
        foreach (var entry in RedactedBeforePaths)
        {
            string? stripped = null;
            if (entry.Length == 0 || string.Equals(entry, segment, StringComparison.Ordinal))
            {
                stripped = string.Empty;
            }
            else if (entry.StartsWith(prefix, StringComparison.Ordinal))
            {
                stripped = entry.Substring(prefix.Length);
            }

            if (stripped is not null)
            {
                nested ??= new List<string>(RedactedBeforePaths.Count);
                nested.Add(stripped);
            }
        }

        return nested is null
            ? this with
            {
                RedactedBeforePaths = Array.Empty<string>(),
            }
            : this with
            {
                RedactedBeforePaths = nested,
            };
    }

    private static bool IsRedactionMatch(string? entry, string pathText)
    {
        if (entry is null)
        {
            return false;
        }

        if (entry.Length == 0)
        {
            return true;
        }

        return string.Equals(entry, pathText, StringComparison.Ordinal)
            || (
                pathText.StartsWith(entry, StringComparison.Ordinal)
                && pathText.Length > entry.Length
                && pathText[entry.Length] == '.'
            );
    }
}
