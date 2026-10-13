using System.ComponentModel;

namespace SparseTypes;

/// <summary>
/// Classifies the exact transition at a queried path.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseFieldChangeKind
{
    /// <summary>No exact transition at the path.</summary>
    Unchanged = 0,

    /// <summary>The value changed while remaining present.</summary>
    Changed = 1,

    /// <summary>The value was added.</summary>
    Added = 2,

    /// <summary>The value was removed.</summary>
    Removed = 3,

    /// <summary>A keyed collection order changed at the member path.</summary>
    Reordered = 4,
}

/// <summary>
/// Point-in-time semantic state for one path within a change set.
/// </summary>
/// <remarks>
/// <para>
/// The state derives from a single retained change set: <see cref="IsChanged"/>
/// reports an exact transition at <see cref="Path"/>, <see cref="HasAncestorChange"/>
/// reports a transition at a strict ancestor (for example a replaced nested
/// object or an added collection), and <see cref="HasDescendantChange"/> reports
/// transitions strictly below the path. <see cref="Before"/> and
/// <see cref="After"/> carry the presence-aware endpoints of the exact
/// transition only; they are both <c>Missing</c> when <see cref="IsChanged"/>
/// is false, which means "no tracked transition", not "absent member".
/// Presence is preserved exactly: <c>Missing</c> never collapses into
/// <c>Present(null)</c>.
/// </para>
/// <para>
/// Lifetime: the state is a snapshot of the change set it was queried from.
/// Change-set queries are point-in-time (change sets are immutable), while
/// edit-session queries diff the live model afresh on every call and never
/// retain subscriptions or per-field baseline caches. A single query scans the
/// flattened change records once, so one query costs O(R) for R records plus
/// the records allocation; session queries additionally pay one baseline diff.
/// Snapshot one change set and reuse it for repeated lookups.
/// </para>
/// </remarks>
/// <typeparam name="T">The addressed value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct SparseFieldState<T>
{
    /// <summary>Gets the queried path.</summary>
    public SparsePath Path { get; internal init; }

    /// <summary>Gets whether the path itself changed between the endpoints.</summary>
    public bool IsChanged { get; internal init; }

    /// <summary>Gets the kind of the exact transition, or Unchanged when absent.</summary>
    public SparseFieldChangeKind Kind { get; internal init; }

    /// <summary>Gets whether a strict ancestor of the path changed.</summary>
    public bool HasAncestorChange { get; internal init; }

    /// <summary>Gets whether a strict descendant of the path changed.</summary>
    public bool HasDescendantChange { get; internal init; }

    /// <summary>Gets whether the path, an ancestor, or a descendant changed.</summary>
    public bool HasChanges => IsChanged || HasAncestorChange || HasDescendantChange;

    /// <summary>
    /// Gets the presence-aware value before the exact transition.
    /// </summary>
    /// <remarks>
    /// Meaningful only when <see cref="IsChanged"/> is true; otherwise
    /// <c>Missing</c>. A replaced ancestor leaves both endpoints
    /// <c>Missing</c> because identity mapping across the replacement is
    /// undefined; query the ancestor path for its own transition.
    /// </remarks>
    public Optional<T> Before { get; internal init; }

    /// <summary>
    /// Gets the presence-aware value after the exact transition.
    /// </summary>
    /// <remarks>
    /// Meaningful only when <see cref="IsChanged"/> is true; otherwise
    /// <c>Missing</c>. See <see cref="Before"/> for replaced-ancestor behavior.
    /// </remarks>
    public Optional<T> After { get; internal init; }
}
