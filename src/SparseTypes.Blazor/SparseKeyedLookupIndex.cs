using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SparseTypes.Blazor;

/// <summary>Positional access over one live collection for index validation.</summary>
/// <remarks>
/// A struct snapshot: the collection reference plus O(1) count and positional
/// reads. Plain enumerables without positional access report
/// <see cref="CanSeek"/> as false and stay on the live linear scan.
/// </remarks>
internal readonly struct KeyedCollectionAccess
{
    private readonly object _collection;
    private readonly IList? _list;
    private readonly PropertyInfo? _readOnlyIndexer;

    /// <summary>Gets the number of elements observed when the access was captured.</summary>
    public int Count { get; }

    /// <summary>Gets whether positional validation reads are available.</summary>
    public bool CanSeek => _list is not null || _readOnlyIndexer is not null;

    private KeyedCollectionAccess(
        object collection,
        IList? list,
        PropertyInfo? readOnlyIndexer,
        int count
    )
    {
        _collection = collection;
        _list = list;
        _readOnlyIndexer = readOnlyIndexer;
        Count = count;
    }

    /// <summary>Captures positional access for a collection, if supported.</summary>
    internal static KeyedCollectionAccess Create(object collection, IEnumerable elements)
    {
        if (elements is IList list)
        {
            return new KeyedCollectionAccess(collection, list, null, list.Count);
        }

        var collectionType = collection.GetType();
        if (
            SparseTypeMetadataCache.TryGetReadOnlyList(
                collectionType,
                out var listInterface,
                out var indexer
            )
            && indexer is not null
            && SparseTypeMetadataCache.TryGetReadOnlyCollectionCount(
                collectionType,
                listInterface.GetGenericArguments()[0],
                collection,
                out var count
            )
        )
        {
            return new KeyedCollectionAccess(collection, null, indexer, count);
        }

        return new KeyedCollectionAccess(collection, null, null, 0);
    }

    /// <summary>Reads the element at a hint position, or null when out of range.</summary>
    internal object? At(int index)
    {
        if (_list is not null)
        {
            return (uint)index < (uint)_list.Count ? _list[index] : null;
        }

        if (_readOnlyIndexer is not null && (uint)index < (uint)Count)
        {
            return _readOnlyIndexer.GetValue(_collection, [index]);
        }

        return null;
    }
}

/// <summary>Key-to-element index scoped to one live collection instance.</summary>
/// <remarks>
/// Entries are held in a <see cref="ConditionalWeakTable{TKey, TValue}"/>
/// keyed by the collection itself, so an index never outlives its collection,
/// never crosses models, and holds no strong root beyond the collection's own
/// lifetime. Every hit is validated against live state (count, positional
/// hint, typed key); any mismatch falls back to a live linear scan that
/// rebuilds the index. The index therefore only ever returns elements and
/// never reports authoritative misses, which keeps removal, replacement,
/// reorder, raw mutation, history, and reconcile behavior identical to the
/// linear scan. Assigned and temporary keys use separate tables so their
/// distinction is preserved; duplicate keys disable indexing for that table
/// until the duplication is gone.
/// </remarks>
internal static class SparseKeyedLookupIndex
{
    private static readonly ConditionalWeakTable<object, CollectionIndex> Indexes = new();

    private sealed class CollectionIndex
    {
        public readonly object Gate = new();
        public SparseTypeMetadataCache.KeyReaderInfo? KeyReader;
        public Dictionary<object, Slot>? ByKey;
        public Func<object, object?>? TemporaryReader;
        public Dictionary<Guid, Slot>? ByTemporary;
        public int Count;
    }

    private readonly record struct Slot(object? Element, int Hint);

    /// <summary>Attempts an index-backed assigned-key lookup.</summary>
    /// <returns>True with the live element on a validated hit; false otherwise.</returns>
    internal static bool TryGetByKey(
        object collection,
        KeyedCollectionAccess access,
        SparseTypeMetadataCache.KeyReaderInfo reader,
        object key,
        out object? element
    )
    {
        element = null;
        if (!access.CanSeek || !Indexes.TryGetValue(collection, out var index))
        {
            return false;
        }

        lock (index.Gate)
        {
            if (
                !ReferenceEquals(index.KeyReader, reader)
                || index.ByKey is null
                || index.Count != access.Count
                || !index.ByKey.TryGetValue(key, out var slot)
            )
            {
                return false;
            }

            // Positional hint plus live key equality prove the slot still
            // answers this key; anything else rebuilds from a live scan.
            var current = access.At(slot.Hint);
            if (
                current is null
                || !ReferenceEquals(current, slot.Element)
                || !Equals(reader.GetKey(current), key)
            )
            {
                return false;
            }

            element = current;
            return true;
        }
    }

    /// <summary>Rebuilds the assigned-key table from a live enumeration.</summary>
    /// <remarks>Duplicate keys leave the table empty so lookups keep exact first-match scan semantics.</remarks>
    internal static void RebuildKeys(
        object collection,
        KeyedCollectionAccess access,
        IEnumerable elements,
        SparseTypeMetadataCache.KeyReaderInfo reader
    )
    {
        if (!access.CanSeek)
        {
            return;
        }

        var table = new Dictionary<object, Slot>();
        var position = 0;
        var duplicated = false;
        foreach (var element in elements)
        {
            if (element is not null)
            {
                var elementKey = reader.GetKey(element);
                if (elementKey is not null && !table.ContainsKey(elementKey))
                {
                    table.Add(elementKey, new Slot(element, position));
                }
                else if (elementKey is not null)
                {
                    duplicated = true;
                }
            }

            position++;
        }

        var index = Indexes.GetValue(collection, static _ => new CollectionIndex());
        lock (index.Gate)
        {
            index.KeyReader = reader;
            index.ByKey = duplicated ? null : table;
            index.Count = access.Count;
        }
    }

    /// <summary>Attempts an index-backed temporary-key lookup.</summary>
    /// <returns>True with the live element on a validated hit; false otherwise.</returns>
    internal static bool TryGetByTemporaryKey(
        object collection,
        KeyedCollectionAccess access,
        Func<object, object?> reader,
        Guid temporaryKey,
        out object? element
    )
    {
        element = null;
        if (!access.CanSeek || !Indexes.TryGetValue(collection, out var index))
        {
            return false;
        }

        lock (index.Gate)
        {
            if (
                !ReferenceEquals(index.TemporaryReader, reader)
                || index.ByTemporary is null
                || index.Count != access.Count
                || !index.ByTemporary.TryGetValue(temporaryKey, out var slot)
            )
            {
                return false;
            }

            var current = access.At(slot.Hint);
            if (
                current is null
                || !ReferenceEquals(current, slot.Element)
                || !Equals(reader(current), temporaryKey)
            )
            {
                return false;
            }

            element = current;
            return true;
        }
    }

    /// <summary>Rebuilds the temporary-key table from a live enumeration.</summary>
    internal static void RebuildTemporaryKeys(
        object collection,
        KeyedCollectionAccess access,
        IEnumerable elements,
        Func<object, object?> reader
    )
    {
        if (!access.CanSeek)
        {
            return;
        }

        var table = new Dictionary<Guid, Slot>();
        var position = 0;
        var duplicated = false;
        foreach (var element in elements)
        {
            if (element is not null && reader(element) is Guid temporaryKey)
            {
                if (!table.ContainsKey(temporaryKey))
                {
                    table.Add(temporaryKey, new Slot(element, position));
                }
                else
                {
                    duplicated = true;
                }
            }

            position++;
        }

        var index = Indexes.GetValue(collection, static _ => new CollectionIndex());
        lock (index.Gate)
        {
            index.TemporaryReader = reader;
            index.ByTemporary = duplicated ? null : table;
            index.Count = access.Count;
        }
    }
}
