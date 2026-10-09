namespace SparseFragments;

/// <summary>Statically specialized structural comparison for known collection shapes.</summary>
/// <remarks>
/// <para>
/// Generated fragment equality already selects a typed comparison per declared member shape
/// (<c>AreSequenceEqual</c>/<c>AreSetEqual</c>/<c>AreDictionaryEqual</c>). These overloads bind
/// that choice to the declared concrete type (<c>T[]</c>, <c>List{T}</c>, <c>HashSet{T}</c>,
/// <c>SortedSet{T}</c>, <c>Dictionary{TKey,TValue}</c>, <c>SortedDictionary{TKey,TValue}</c>,
/// <c>SortedList{TKey,TValue}</c>) at compile time, so member comparisons skip runtime shape
/// classification, comparer/count/entry reflection, and dictionary-view construction.
/// Comparers are read through the statically known <c>Comparer</c> property instead of
/// <c>Type.GetProperty</c>.
/// </para>
/// <para>
/// Fallback conditions (unchanged semantics, shared with the <c>IEnumerable</c> overloads):
/// members declared as <c>object</c>, collection interfaces, or unknown/custom implementations
/// keep the dynamic paths in <see cref="SparseValueComparer"/> and
/// <c>FragmentComparisonPrimitives</c>. Derived <c>List{T}</c> instances keep their
/// non-generic <c>IList</c> comparison view via the object fallback. Comparer veto semantics
/// are preserved: when both sides expose a comparer and they differ, the collections are
/// unequal regardless of operand order.
/// </para>
/// <para>
/// Element/value equality always flows through <see cref="SparseValueComparer.AreEqual"/>
/// (framework-default scalar equality plus structural semantics) or a caller-provided
/// generated semantic comparer, so custom <c>SparseCompare</c> member rules and fragment
/// Diff/ChangeSet paths observe identical rules on every path. Trim/NativeAOT clean: no
/// reflection and no runtime generic code generation.
/// </para>
/// </remarks>
internal static class SparseConcreteComparisons
{
    /// <summary>Compares arrays in order without runtime shape probing.</summary>
    public static bool AreSequenceEqual<T>(T[]? left, T[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!SparseValueComparer.AreEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares lists in order without runtime shape probing.</summary>
    /// <remarks>
    /// Derived lists keep their non-generic <c>IList</c> comparison view: non-exact
    /// runtime types fall back to the object path, matching the typed sequence behavior.
    /// </remarks>
    public static bool AreSequenceEqual<T>(List<T>? left, List<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.GetType() != typeof(List<T>) || right.GetType() != typeof(List<T>))
        {
            return SparseValueComparer.AreEqual((object)left, (object)right);
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!SparseValueComparer.AreEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares an array against a list in order without runtime shape probing.</summary>
    public static bool AreSequenceEqual<T>(T[]? left, List<T>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (right.GetType() != typeof(List<T>))
        {
            return SparseValueComparer.AreEqual((object)left, (object)right);
        }

        if (left.Length != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!SparseValueComparer.AreEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares a list against an array in order without runtime shape probing.</summary>
    public static bool AreSequenceEqual<T>(List<T>? left, T[]? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.GetType() != typeof(List<T>))
        {
            return SparseValueComparer.AreEqual((object)left, (object)right);
        }

        if (left.Count != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!SparseValueComparer.AreEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares arrays with a generated semantic item comparer.</summary>
    public static bool AreSequenceEqual<T>(T[]? left, T[]? right, Func<T, T, bool> itemComparer)
    {
        ArgumentNullException.ThrowIfNull(itemComparer);
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!itemComparer(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares lists with a generated semantic item comparer.</summary>
    public static bool AreSequenceEqual<T>(
        List<T>? left,
        List<T>? right,
        Func<T, T, bool> itemComparer
    )
    {
        ArgumentNullException.ThrowIfNull(itemComparer);
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.GetType() != typeof(List<T>) || right.GetType() != typeof(List<T>))
        {
            return SparseValueComparer.AreSequenceEqual(
                (IEnumerable<T>)left,
                (IEnumerable<T>)right,
                itemComparer
            );
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!itemComparer(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares hash sets order-independently with static comparer access.</summary>
    /// <remarks>Differing comparers veto equality in both directions, as on the dynamic path.</remarks>
    public static bool AreSetEqual<T>(HashSet<T>? left, HashSet<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        return left.SetEquals(right);
    }

    /// <summary>Compares sorted sets order-independently with static comparer access.</summary>
    /// <remarks>Differing comparers veto equality in both directions, as on the dynamic path.</remarks>
    public static bool AreSetEqual<T>(SortedSet<T>? left, SortedSet<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        return left.SetEquals(right);
    }

    /// <summary>Compares dictionaries by key/value semantics with static comparer access.</summary>
    /// <remarks>
    /// Differing key comparers veto equality in both directions, as on the dynamic path.
    /// Lookups honor each dictionary's own comparer; values use default sparse semantics.
    /// </remarks>
    public static bool AreDictionaryEqual<TKey, TValue>(
        Dictionary<TKey, TValue>? left,
        Dictionary<TKey, TValue>? right
    )
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (
                !right.TryGetValue(pair.Key, out var rightValue)
                || !SparseValueComparer.AreEqual(pair.Value, rightValue)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares sorted dictionaries by key/value semantics with static comparer access.</summary>
    /// <remarks>Differing key comparers veto equality in both directions, as on the dynamic path.</remarks>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedDictionary<TKey, TValue>? left,
        SortedDictionary<TKey, TValue>? right
    )
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (
                !right.TryGetValue(pair.Key, out var rightValue)
                || !SparseValueComparer.AreEqual(pair.Value, rightValue)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares sorted lists by key/value semantics with static comparer access.</summary>
    /// <remarks>Differing key comparers veto equality in both directions, as on the dynamic path.</remarks>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedList<TKey, TValue>? left,
        SortedList<TKey, TValue>? right
    )
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (
                !right.TryGetValue(pair.Key, out var rightValue)
                || !SparseValueComparer.AreEqual(pair.Value, rightValue)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares dictionaries with a generated semantic value comparer.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        Dictionary<TKey, TValue>? left,
        Dictionary<TKey, TValue>? right,
        Func<TValue, TValue, bool> valueComparer
    )
    {
        ArgumentNullException.ThrowIfNull(valueComparer);
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (
                !right.TryGetValue(pair.Key, out var rightValue)
                || !valueComparer(pair.Value, rightValue)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares sorted dictionaries with a generated semantic value comparer.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedDictionary<TKey, TValue>? left,
        SortedDictionary<TKey, TValue>? right,
        Func<TValue, TValue, bool> valueComparer
    )
    {
        ArgumentNullException.ThrowIfNull(valueComparer);
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (
                !right.TryGetValue(pair.Key, out var rightValue)
                || !valueComparer(pair.Value, rightValue)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares sorted lists with a generated semantic value comparer.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedList<TKey, TValue>? left,
        SortedList<TKey, TValue>? right,
        Func<TValue, TValue, bool> valueComparer
    )
    {
        ArgumentNullException.ThrowIfNull(valueComparer);
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        if (!Equals(left.Comparer, right.Comparer))
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (
                !right.TryGetValue(pair.Key, out var rightValue)
                || !valueComparer(pair.Value, rightValue)
            )
            {
                return false;
            }
        }

        return true;
    }
}
