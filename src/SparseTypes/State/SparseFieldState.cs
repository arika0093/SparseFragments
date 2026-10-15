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
/// <remarks>The state derives from a single retained change set.</remarks>
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
    /// <remarks>Meaningful only when <see cref="IsChanged"/> is true; otherwise <c>Missing</c>.</remarks>
    public Optional<T> Before { get; internal init; }

    /// <summary>
    /// Gets the presence-aware value after the exact transition.
    /// </summary>
    /// <remarks>Meaningful only when <see cref="IsChanged"/> is true; otherwise <c>Missing</c>.</remarks>
    public Optional<T> After { get; internal init; }
}
