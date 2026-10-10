using System.ComponentModel;

namespace SparseFragments;

/// <summary>
/// One effective value attributed to its contributing origin.
/// </summary>
/// <remarks>
/// Provenance is explanatory metadata, not semantic state: entries describe the
/// merged effective values for inspection (for example a configuration viewer),
/// never the hidden overwritten layers. A <c>null</c> origin means
/// <c>Unknown</c>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct FragmentOriginEntry
{
    /// <summary>The path of the effective value.</summary>
    public SparsePath Path { get; init; }

    /// <summary>The contributing origin, or <c>null</c> for Unknown.</summary>
    public string? Origin { get; init; }
}

/// <summary>
/// The read-only effective values attributed to one origin.
/// </summary>
/// <remarks>
/// Grouping is intentionally lossy: a group exposes only the effective values
/// attributed to its origin. Regrouping or remerging groups is not guaranteed
/// to reconstruct the original layers or preserve collection ordering.
/// </remarks>
/// <typeparam name="TFragment">The generated fragment type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct FragmentOriginGroup<TFragment>
{
    /// <summary>Creates an origin group.</summary>
    /// <param name="origin">The contributing origin, or <c>null</c> for Unknown.</param>
    /// <param name="fragment">The read-only effective projection.</param>
    public FragmentOriginGroup(string? origin, TFragment fragment)
    {
        Origin = origin;
        Fragment = fragment;
    }

    /// <summary>The contributing origin, or <c>null</c> for Unknown.</summary>
    public string? Origin { get; init; }

    /// <summary>The read-only effective projection for this origin.</summary>
    public TFragment Fragment { get; init; }
}
