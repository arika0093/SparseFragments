using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

public enum AppendRebaseStorage
{
    List,
    Array,
    ReadOnly,
}

/// <summary>Measures append replay capacity for collection and enumerable copy paths.</summary>
[MemoryDiagnoser]
public class AppendRebaseCapacityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(1, 64)]
    public int AddCount { get; set; }

    [Params(AppendRebaseStorage.List, AppendRebaseStorage.Array, AppendRebaseStorage.ReadOnly)]
    public AppendRebaseStorage Storage { get; set; }

    private IReadOnlyList<int> _before = null!;
    private IReadOnlyList<int> _desired = null!;
    private IReadOnlyList<int> _current = null!;
    private IReadOnlyList<object?> _objectBefore = null!;
    private IReadOnlyList<object?> _objectDesired = null!;
    private IReadOnlyList<object?> _objectCurrent = null!;
    private static readonly Func<object?, object?, bool> Equal = object.Equals;

    [GlobalSetup]
    public void Setup()
    {
        var empty = System.Array.Empty<int>();
        if (
            !SparseFragmentRuntime.TryRebaseSequenceAppendArray(
                empty,
                empty,
                empty,
                null,
                out var emptyResult,
                out _
            ) || !ReferenceEquals(empty, emptyResult)
        )
        {
            throw new InvalidOperationException("Empty array replay must reuse the empty array.");
        }
        var before = Enumerable.Range(0, Size).ToArray();
        var desired = before.Concat(Enumerable.Range(Size, AddCount)).ToArray();
        var current = before.Concat([-1]).ToArray();
        _before = Store(before);
        _desired = Store(desired);
        _current = Store(current);
        _objectBefore = Store(before.Cast<object?>().ToArray());
        _objectDesired = Store(desired.Cast<object?>().ToArray());
        _objectCurrent = Store(current.Cast<object?>().ToArray());
        var expected = current.Concat(desired.Skip(Size)).ToArray();
        var typed = Typed();
        var array = Array();
        var boxed = Object();
        if (
            !typed.SequenceEqual(expected)
            || !array.SequenceEqual(expected)
            || !boxed.SequenceEqual(expected.Cast<object?>())
            || !_before.SequenceEqual(before)
            || !_desired.SequenceEqual(desired)
            || !_current.SequenceEqual(current)
            || !_objectBefore.SequenceEqual(before.Cast<object?>())
            || !_objectDesired.SequenceEqual(desired.Cast<object?>())
            || !_objectCurrent.SequenceEqual(current.Cast<object?>())
        )
        {
            throw new InvalidOperationException(
                "Append replay must preserve ordered suffixes and inputs."
            );
        }
        if (
            !SparseFragmentRuntime.TryRebaseSequenceAppend(
                _before,
                _desired,
                _desired,
                null,
                out var applied,
                out var appliedReason
            )
            || appliedReason is not null
            || !applied.SequenceEqual(desired)
            || !SparseFragmentRuntime.TryRebaseSequenceAppendArray(
                _before,
                _desired,
                _desired,
                null,
                out var arrayApplied,
                out _
            )
            || !arrayApplied.SequenceEqual(desired)
            || !SparseFragmentRuntime.TryRebaseAppend(
                _objectBefore,
                _objectDesired,
                _objectDesired,
                Equal,
                out var objectApplied,
                out _
            )
            || !ReferenceEquals(objectApplied, _objectDesired)
        )
        {
            throw new InvalidOperationException(
                "Already-applied replay must retain its existing behavior."
            );
        }
        var changed = Store(current.Select(value => value == 0 ? -2 : value).ToArray());
        var objectChanged = Store(changed.Cast<object?>().ToArray());
        if (
            SparseFragmentRuntime.TryRebaseSequenceAppend(
                _before,
                _desired,
                changed,
                null,
                out _,
                out var reason
            )
            || reason is null
            || SparseFragmentRuntime.TryRebaseSequenceAppendArray(
                _before,
                _desired,
                changed,
                null,
                out _,
                out var arrayReason
            )
            || arrayReason is null
            || SparseFragmentRuntime.TryRebaseAppend(
                _objectBefore,
                _objectDesired,
                objectChanged,
                Equal,
                out _,
                out var objectReason
            )
            || objectReason is null
        )
        {
            throw new InvalidOperationException(
                "Changed prefixes must conflict in both append paths."
            );
        }
    }

    private IReadOnlyList<T> Store<T>(T[] values) =>
        Storage switch
        {
            AppendRebaseStorage.Array => values,
            AppendRebaseStorage.List => values.ToList(),
            _ => new ReadOnlySequence<T>(values),
        };

    [Benchmark]
    public List<int> Typed()
    {
        if (
            !SparseFragmentRuntime.TryRebaseSequenceAppend(
                _before,
                _desired,
                _current,
                null,
                out var result,
                out _
            )
        )
        {
            throw new InvalidOperationException("Append replay unexpectedly conflicted.");
        }
        return result;
    }

    [Benchmark]
    public int[] Array()
    {
        if (
            !SparseFragmentRuntime.TryRebaseSequenceAppendArray(
                _before,
                _desired,
                _current,
                null,
                out var result,
                out _
            )
        )
        {
            throw new InvalidOperationException("Array append replay unexpectedly conflicted.");
        }
        return result;
    }

    [Benchmark]
    public IReadOnlyList<object?> Object()
    {
        if (
            !SparseFragmentRuntime.TryRebaseAppend(
                _objectBefore,
                _objectDesired,
                _objectCurrent,
                Equal,
                out var result,
                out _
            )
        )
        {
            throw new InvalidOperationException("Append replay unexpectedly conflicted.");
        }
        return result;
    }

    private sealed class ReadOnlySequence<T>(T[] values) : IReadOnlyList<T>
    {
        public int Count => values.Length;
        public T this[int index] => values[index];

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
