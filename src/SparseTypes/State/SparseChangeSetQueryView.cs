using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>
/// Reusable, immutable query view over one captured change set.
/// </summary>
/// <remarks>
/// The view flattens <see cref="ISparseChangeSet.EnumerateChangeRecords"/> once on first
/// use and indexes structured paths in a segment trie, so repeated field queries cost
/// path depth instead of one full projection and scan per field. One-off consumers keep
/// using the straightforward <see cref="SparseStateQueries"/> scan without paying for an
/// index. This is the single snapshot API shared by Blazor refresh generations and
/// repeated point-in-time lookups; there is no second index API.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseChangeSetQueryView
{
    private readonly ISparseChangeSet _changes;
    private readonly object _sync = new();
    private IReadOnlyList<SparseChangeRecord>? _records;
    private Dictionary<Type, TrieNode>? _roots;
    private int _projectionCount;

    /// <summary>Captures a reusable query view over one change set.</summary>
    /// <param name="changes">The change set to inspect; retained, never mutated.</param>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    public SparseChangeSetQueryView(ISparseChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _changes = changes;
    }

    /// <summary>Captures a reusable query view over one change set.</summary>
    /// <param name="changes">The change set to inspect; retained, never mutated.</param>
    /// <returns>A view serving repeated field queries over the captured set.</returns>
    /// <remarks>
    /// Capture copies the flattened records on first query, so custom
    /// <see cref="ISparseChangeSet"/> implementations that expose changing data are
    /// observed once; later mutations are not reflected.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    public static SparseChangeSetQueryView Capture(ISparseChangeSet changes) => new(changes);

    /// <summary>Gets the captured change set this view inspects.</summary>
    public ISparseChangeSet Changes => _changes;

    /// <summary>Gets how many times the view flattened the change set.</summary>
    /// <remarks>
    /// Instrumentation for benchmarks: a reused view projects once no matter how
    /// many fields query it.
    /// </remarks>
    public int ProjectionCount
    {
        get
        {
            lock (_sync)
            {
                return _projectionCount;
            }
        }
    }

    /// <summary>Gets the flattened records, projected once and retained.</summary>
    /// <remarks>In enumeration order; the same instances back every indexed query.</remarks>
    public IReadOnlyList<SparseChangeRecord> Records
    {
        get
        {
            lock (_sync)
            {
                return _records ??= FlattenLocked();
            }
        }
    }

    /// <summary>Builds the flattened projection and segment index now.</summary>
    /// <remarks>Queries build both lazily; call this to separate construction from warmed reads.</remarks>
    public void EnsureIndexed()
    {
        lock (_sync)
        {
            _records ??= FlattenLocked();
            _roots ??= BuildIndexLocked(_records);
        }
    }

    /// <summary>Queries the semantic field state at a typed path over the captured set.</summary>
    /// <param name="path">The typed path to inspect.</param>
    /// <typeparam name="TModel">The root model type.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    /// <returns>The point-in-time field state.</returns>
    /// <remarks>
    /// First-record-wins for repeated exact paths, presence-aware endpoints, and
    /// root-type separation all match the <see cref="SparseStateQueries"/> scan.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The exact transition value cannot be represented as <typeparamref name="TValue"/>.
    /// </exception>
    public SparseFieldState<TValue> FieldState<TModel, TValue>(SparsePath<TModel, TValue> path) =>
        FieldStateCore<TValue>(path.Path);

    /// <summary>Queries the semantic field state at a model-agnostic path over the captured set.</summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns>The point-in-time field state with boxed endpoints.</returns>
    /// <remarks>Covers paths without a compile-time typed form. Unknown paths report clean.</remarks>
    public SparseFieldState<object?> FieldState(SparsePath path) => FieldStateCore<object?>(path);

    /// <summary>Builds a collection state view over an already-obtained keyed-sequence transition.</summary>
    /// <param name="change">The member's typed keyed transition.</param>
    /// <param name="path">The collection member path the view reports.</param>
    /// <typeparam name="TKey">The stable key type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <typeparam name="TElementChange">The element change-set type.</typeparam>
    /// <returns>A point-in-time view retaining <paramref name="change"/>.</returns>
    /// <remarks>Pass-through over the retained snapshot: no new diff is derived.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is null.</exception>
    public static SparseCollectionState KeyedState<TKey, TElement, TElementChange>(
        ISparseKeyedChange<TKey, TElement, TElementChange> change,
        SparsePath path
    ) => change.KeyedState(path);

    /// <summary>Builds a collection state view over an already-obtained dictionary transition.</summary>
    /// <param name="change">The member's typed dictionary transition.</param>
    /// <param name="path">The collection member path the view reports.</param>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <typeparam name="TEdited">The edited-projection type.</typeparam>
    /// <returns>A point-in-time view retaining <paramref name="change"/>.</returns>
    /// <remarks>Pass-through over the retained snapshot: no new diff is derived.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is null.</exception>
    public static SparseCollectionState DictionaryState<TKey, TValue, TEdited>(
        ISparseDictionaryChange<TKey, TValue, TEdited> change,
        SparsePath path
    ) => change.DictionaryState(path);

    private SparseFieldState<T> FieldStateCore<T>(SparsePath query)
    {
        Dictionary<Type, TrieNode> roots;
        lock (_sync)
        {
            _records ??= FlattenLocked();
            roots = _roots ??= BuildIndexLocked(_records);
        }

        if (!roots.TryGetValue(query.RootType, out var node))
        {
            return SparseStateQueries.Assemble<T>(query, null, false, false);
        }

        if (query.IsRoot)
        {
            return SparseStateQueries.Assemble<T>(query, node.Exact, false, HasChildRecords(node));
        }

        // Each node visited before the terminal step is a strict prefix of the
        // query, so an exact record there is an ancestor change.
        var ancestor = node.Exact is not null;
        var segments = query.Segments;
        for (var index = 0; index < segments.Count; index++)
        {
            if (node.Children is null || !node.Children.TryGetValue(segments[index], out var child))
            {
                return SparseStateQueries.Assemble<T>(query, null, ancestor, false);
            }

            node = child;
            if (index < segments.Count - 1 && node.Exact is not null)
            {
                ancestor = true;
            }
        }

        return SparseStateQueries.Assemble<T>(query, node.Exact, ancestor, HasChildRecords(node));
    }

    private static bool HasChildRecords(TrieNode node)
    {
        // A terminal exact record is the query hit, never its own descendant.
        if (node.Children is null)
        {
            return false;
        }

        foreach (var child in node.Children.Values)
        {
            if (child.SubtreeHasRecords)
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<SparseChangeRecord> FlattenLocked()
    {
        _projectionCount++;
        return _changes.EnumerateChangeRecords().ToArray();
    }

    private static Dictionary<Type, TrieNode> BuildIndexLocked(
        IReadOnlyList<SparseChangeRecord> records
    )
    {
        // Index by structured segments, never by ToString: distinct typed keys
        // can share display text.
        var roots = new Dictionary<Type, TrieNode>();
        foreach (var record in records)
        {
            if (!roots.TryGetValue(record.Path.RootType, out var node))
            {
                node = new TrieNode();
                roots.Add(record.Path.RootType, node);
            }

            node.SubtreeHasRecords = true;
            if (record.Path.IsRoot)
            {
                node.Exact ??= record;
                continue;
            }

            foreach (var segment in record.Path.Segments)
            {
                node.Children ??= new Dictionary<SparsePathSegment, TrieNode>();
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new TrieNode();
                    node.Children.Add(segment, child);
                }

                node = child;
                node.SubtreeHasRecords = true;
            }

            // First record wins for repeated exact paths, matching the scan.
            node.Exact ??= record;
        }

        return roots;
    }

    private sealed class TrieNode
    {
        public SparseChangeRecord? Exact;
        public Dictionary<SparsePathSegment, TrieNode>? Children;
        public bool SubtreeHasRecords;
    }
}
