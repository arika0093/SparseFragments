using System.ComponentModel;

namespace SparseFragments;

/// <summary>Model-independent Patch and ChangeSet operation kernels.</summary>
/// <remarks>
/// Pure generic calculations over keys, operation kinds, orders and comparers:
/// presence composition, keyed add/remove inversion and composition, order-only
/// transitions, empty-operation identities and canonical conflict-path
/// reporting. No model CLR types, no reflection; all dispatch is
/// type-safe generic code so NativeAOT trimming stays clean. Model-specific
/// emitters keep typed nested fragments, sparse canonical storage, member
/// policies and ownership boundaries; they invoke these kernels for the
/// reusable subset (see the Shared kernel plan for the inventory).
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseOperationKernels
{
    /// <summary>Composes scalar presence across two sequential transitions.</summary>
    /// <param name="firstBeforePresent">Whether the first transition has a before value.</param>
    /// <param name="firstAfterPresent">Whether the first transition has an after value.</param>
    /// <param name="secondAfterPresent">Whether the second transition has an after value.</param>
    /// <param name="resultBeforePresent">Composed before presence.</param>
    /// <param name="resultAfterPresent">Composed after presence.</param>
    /// <returns>True when the composed transition is non-empty.</returns>
    public static bool TryComposePresence(
        bool firstBeforePresent,
        bool firstAfterPresent,
        bool secondAfterPresent,
        out bool resultBeforePresent,
        out bool resultAfterPresent
    )
    {
        resultBeforePresent = firstBeforePresent;
        resultAfterPresent = secondAfterPresent;
        if (!resultBeforePresent && !resultAfterPresent)
        {
            // Absent throughout (including add-then-remove): identity.
            return false;
        }

        if (resultBeforePresent && resultAfterPresent)
        {
            // Present throughout: identity only when the intermediate never
            // left; remove-then-add needs a value-level check upstream.
            return firstAfterPresent != resultBeforePresent;
        }

        return true;
    }

    /// <summary>Whether a transition is the empty (identity) operation.</summary>
    /// <param name="beforePresent">Whether a before value is present.</param>
    /// <param name="afterPresent">Whether an after value is present.</param>
    /// <param name="orderChanged">Whether keyed order changed.</param>
    /// <returns>True for the identity operation.</returns>
    public static bool IsEmptyTransition(
        bool beforePresent,
        bool afterPresent,
        bool orderChanged
    ) => beforePresent == afterPresent && !orderChanged;

    /// <summary>Finds the first index with an equal assigned key.</summary>
    /// <param name="keys">Keys in positional order.</param>
    /// <param name="key">Key to locate.</param>
    /// <param name="comparer">Key equality, or null for the default.</param>
    /// <returns>The index, or -1 when absent.</returns>
    public static int IndexOfKey<TKey>(
        IReadOnlyList<TKey> keys,
        TKey key,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        comparer ??= EqualityComparer<TKey>.Default;
        for (var index = 0; index < keys.Count; index++)
        {
            if (comparer.Equals(keys[index], key))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Determines whether a key is the configured unassigned marker.</summary>
    /// <param name="key">Key to test.</param>
    /// <param name="unassigned">Configured marker.</param>
    /// <param name="comparer">Key equality, or null for the default.</param>
    /// <returns>True when the key equals the marker.</returns>
    public static bool IsUnassignedKey<TKey>(
        TKey key,
        TKey unassigned,
        IEqualityComparer<TKey>? comparer = null
    ) => (comparer ?? EqualityComparer<TKey>.Default).Equals(key, unassigned);

    /// <summary>Composes keyed add/remove sets across two sequential transitions.</summary>
    /// <remarks>
    /// Pure set algebra: a key added then removed vanishes; removed then added
    /// becomes a retained (possibly moved) key. Order-only changes are reported
    /// separately through <paramref name="orderChanged"/>. Pre-sizes outputs;
    /// no LINQ, no boxing for value keys.
    /// </remarks>
    /// <param name="firstAdded">Keys added by the first transition.</param>
    /// <param name="firstRemoved">Keys removed by the first transition.</param>
    /// <param name="secondAdded">Keys added by the second transition.</param>
    /// <param name="secondRemoved">Keys removed by the second transition.</param>
    /// <param name="added">Composed added keys.</param>
    /// <param name="removed">Composed removed keys.</param>
    /// <param name="orderChanged">Whether either side reordered retained keys.</param>
    /// <param name="comparer">Key equality, or null for the default.</param>
    public static void ComposeKeyedSets<TKey>(
        IReadOnlyList<TKey> firstAdded,
        IReadOnlyList<TKey> firstRemoved,
        IReadOnlyList<TKey> secondAdded,
        IReadOnlyList<TKey> secondRemoved,
        out List<TKey> added,
        out List<TKey> removed,
        out bool orderChanged,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(firstAdded);
        ArgumentNullException.ThrowIfNull(firstRemoved);
        ArgumentNullException.ThrowIfNull(secondAdded);
        ArgumentNullException.ThrowIfNull(secondRemoved);
        comparer ??= EqualityComparer<TKey>.Default;

        var firstAddedSet = new HashSet<TKey>(firstAdded, comparer);
        var firstRemovedSet = new HashSet<TKey>(firstRemoved, comparer);
        added = new List<TKey>(firstAdded.Count + secondAdded.Count);
        removed = new List<TKey>(firstRemoved.Count + secondRemoved.Count);

        // Retained first-adds survive unless the second transition removes them.
        // Index loops (not LINQ) keep this allocation-free on the hot path.
        for (var i = 0; i < firstAdded.Count; i++)
        {
            var key = firstAdded[i];
            if (!Contains(secondRemoved, key, comparer))
            {
                added.Add(key);
            }
        }

        // Second-adds survive unless they merely re-add a first-removed key
        // that the first side had already removed (then both cancel when the
        // key was absent at baseline); otherwise a remove+add is a retain.
        for (var i = 0; i < secondAdded.Count; i++)
        {
            var key = secondAdded[i];
            if (firstRemovedSet.Contains(key) && !firstAddedSet.Contains(key))
            {
                continue;
            }

            if (!ListContains(added, key, comparer))
            {
                added.Add(key);
            }
        }

        for (var i = 0; i < firstRemoved.Count; i++)
        {
            var key = firstRemoved[i];
            if (!Contains(secondAdded, key, comparer))
            {
                removed.Add(key);
            }
        }

        for (var i = 0; i < secondRemoved.Count; i++)
        {
            var key = secondRemoved[i];
            if (!Contains(firstAdded, key, comparer) && !ListContains(removed, key, comparer))
            {
                removed.Add(key);
            }
        }

        // Order follows the post-cancellation net adds: cancelled add-then-remove
        // leaves no keys behind (scalar TryComposePresence identity), while
        // surviving net-new or re-added retained keys still affect order.
        orderChanged = added.Count != 0;
    }

    /// <summary>Inverts keyed membership: added and removed swap.</summary>
    public static void InvertKeyedSets<TKey>(
        IReadOnlyList<TKey> added,
        IReadOnlyList<TKey> removed,
        out List<TKey> invertedAdded,
        out List<TKey> invertedRemoved
    )
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(removed);
        invertedAdded = new List<TKey>(removed);
        invertedRemoved = new List<TKey>(added);
    }

    /// <summary>Whether a keyed membership delta is the identity operation.</summary>
    public static bool IsEmptyKeyedSet<TKey>(
        IReadOnlyList<TKey> added,
        IReadOnlyList<TKey> removed,
        bool orderChanged
    )
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(removed);
        return added.Count == 0 && removed.Count == 0 && !orderChanged;
    }

    /// <summary>Escapes one key segment to canonical bracket-key text.</summary>
    /// <remarks>
    /// Matches enumeration and path conventions: invariant-culture text,
    /// double-quoted with backslash and quote escaping.
    /// </remarks>
    /// <param name="keyText">Invariant key text.</param>
    /// <returns>The escaped segment.</returns>
    public static string EscapeKeySegment(string keyText)
    {
        ArgumentNullException.ThrowIfNull(keyText);
        return "\"" + keyText.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Formats a canonical keyed member path.</summary>
    /// <param name="member">Member name.</param>
    /// <param name="keyText">Invariant key text.</param>
    /// <returns>For example <c>Items["k"]</c>.</returns>
    public static string FormatKeyedPath(string member, string keyText)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(keyText);
        return member + "[" + EscapeKeySegment(keyText) + "]";
    }

    /// <summary>Whether either path is a strict segment ancestor of the other.</summary>
    /// <remarks>
    /// Canonical subset of the mixed-operation path algebra: segments split on
    /// '.' with bracket suffixes kept on their element, so <c>A</c> is not an
    /// ancestor of <c>AB</c> while <c>Nested</c> is an ancestor of
    /// <c>Nested.Host</c>.
    /// </remarks>
    public static bool IsAncestorOrDescendant(string first, string second) =>
        IsStrictAncestor(first, second) || IsStrictAncestor(second, first);

    private static bool Contains<TKey>(
        IReadOnlyList<TKey> keys,
        TKey key,
        IEqualityComparer<TKey> comparer
    )
    {
        for (var index = 0; index < keys.Count; index++)
        {
            if (comparer.Equals(keys[index], key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ListContains<TKey>(
        List<TKey> keys,
        TKey key,
        IEqualityComparer<TKey> comparer
    )
    {
        for (var index = 0; index < keys.Count; index++)
        {
            if (comparer.Equals(keys[index], key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStrictAncestor(string ancestor, string descendant)
    {
        var a = SplitPathSegments(ancestor);
        var d = SplitPathSegments(descendant);
        if (a.Length >= d.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], d[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] SplitPathSegments(string path)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        var inQuotes = false;
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '"' && (i == 0 || path[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }

            if (!inQuotes)
            {
                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                }
                else if (c == '.' && depth == 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                    continue;
                }
            }

            current.Append(c);
        }

        segments.Add(current.ToString());
        return segments.ToArray();
    }
}
