using System.Collections;

namespace SparseFragments.Blazor.Tests;

// Wraps a dictionary behind IReadOnlyDictionary only, so field-path tests
// prove the read-only resolution branch instead of the legacy IDictionary one.
public sealed class ReadOnlyDictionaryStub<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> inner)
    : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    public TValue this[TKey key] => inner[key];

    public IEnumerable<TKey> Keys => inner.Keys;

    public IEnumerable<TValue> Values => inner.Values;

    public int Count => inner.Count;

    public bool ContainsKey(TKey key) => inner.ContainsKey(key);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => inner.GetEnumerator();

    public bool TryGetValue(TKey key, out TValue value) => inner.TryGetValue(key, out value!);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Wraps a list behind IReadOnlyList only, so field-path tests prove the
// read-only resolution branch instead of the legacy IList one.
public sealed class ReadOnlyListStub<T>(IReadOnlyList<T> inner) : IReadOnlyList<T>
{
    public T this[int index] => inner[index];

    public int Count => inner.Count;

    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
