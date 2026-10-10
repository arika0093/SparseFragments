using System.ComponentModel;

namespace SparseFragments;

/// <summary>Exact-match lookup over rebase conflicts by typed path.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparsePathQueries
{
    /// <summary>Finds the first conflict at exactly the given path, or null when absent.</summary>
    /// <param name="conflicts">The conflicts to search.</param>
    /// <param name="path">The exact path to locate.</param>
    /// <remarks>Exact paths only; ancestors and descendants never match.</remarks>
    public static SparseConflict? Find(
        this IReadOnlyList<SparseConflict> conflicts,
        SparsePath path
    )
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(path);
        return conflicts.FirstOrDefault(conflict => conflict.Path.Equals(path));
    }

    /// <summary>Finds the first conflict at exactly the given typed path, or null when absent.</summary>
    /// <param name="conflicts">The conflicts to search.</param>
    /// <param name="path">The exact typed path to locate.</param>
    /// <typeparam name="TModel">The root model type.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    /// <remarks>Exact paths only; ancestors and descendants never match.</remarks>
    public static SparseConflict? Find<TModel, TValue>(
        this IReadOnlyList<SparseConflict> conflicts,
        SparsePath<TModel, TValue> path
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        return Find(conflicts, path.Path);
    }
}
