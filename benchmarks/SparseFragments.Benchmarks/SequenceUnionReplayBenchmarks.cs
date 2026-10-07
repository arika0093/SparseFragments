using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

/// <summary>Checks sequence union replay with duplicate contributions and comparer variants.</summary>
[MemoryDiagnoser]
public class SequenceUnionReplayBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(1, 64)]
    public int AddCount { get; set; }

    [Params(false, true)]
    public bool CustomComparer { get; set; }

    [Params(false, true)]
    public bool AlreadyApplied { get; set; }

    private List<string> _before = null!;
    private List<string> _desired = null!;
    private List<string> _current = null!;
    private IEqualityComparer<string> _comparer = null!;

    [GlobalSetup]
    public void Setup()
    {
        var empty = System.Array.Empty<string>();
        if (
            !SparseFragmentRuntime.TryRebaseSequenceSetUnionArray(
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
        _comparer = CustomComparer ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _before = Enumerable.Range(0, Size).Select(index => "base-" + index).ToList();
        _desired = new List<string>(_before);
        _current = new List<string>(_before) { "remote", "remote", _before[0] };
        var additions = new List<string>();
        for (var index = 0; index < AddCount; index++)
        {
            var value = "local-" + index;
            additions.Add(value);
            _desired.Add(value);
            _desired.Add(CustomComparer ? value.ToUpperInvariant() : value);
            _desired.Add(value);
            if (AlreadyApplied)
            {
                _current.Add(CustomComparer ? value.ToUpperInvariant() : value);
            }
        }
        var beforeCopy = _before.ToArray();
        var desiredCopy = _desired.ToArray();
        var currentCopy = _current.ToArray();
        var expected = AlreadyApplied ? currentCopy : currentCopy.Concat(additions).ToArray();
        var actual = Rebase();
        var array = ArrayRebase();
        if (
            !actual.SequenceEqual(expected)
            || !array.SequenceEqual(expected)
            || !_before.SequenceEqual(beforeCopy)
            || !_desired.SequenceEqual(desiredCopy)
            || !_current.SequenceEqual(currentCopy)
        )
        {
            throw new InvalidOperationException(
                "Union replay must preserve current duplicates and append the first local occurrence."
            );
        }
        var removed = _before.Skip(1).ToList();
        if (
            !SparseFragmentRuntime.TryRebaseSequenceSetUnion(
                _before,
                removed,
                _before,
                _comparer,
                out var clean,
                out var reason
            )
            || reason is not null
            || !clean.SequenceEqual(removed)
            || !SparseFragmentRuntime.TryRebaseSequenceSetUnionArray(
                _before,
                removed,
                _before,
                _comparer,
                out var arrayClean,
                out _
            )
            || !arrayClean.SequenceEqual(removed)
            || SparseFragmentRuntime.TryRebaseSequenceSetUnion(
                _before,
                removed,
                _current,
                _comparer,
                out _,
                out var conflictReason
            )
            || conflictReason is null
            || SparseFragmentRuntime.TryRebaseSequenceSetUnionArray(
                _before,
                removed,
                _current,
                _comparer,
                out _,
                out var arrayConflictReason
            )
            || arrayConflictReason is null
        )
        {
            throw new InvalidOperationException(
                "Removal replay must succeed on an unchanged state and conflict beside a concurrent change."
            );
        }
    }

    [Benchmark]
    public List<string> Rebase()
    {
        if (
            !SparseFragmentRuntime.TryRebaseSequenceSetUnion(
                _before,
                _desired,
                _current,
                _comparer,
                out var result,
                out _
            )
        )
        {
            throw new InvalidOperationException("Addition replay unexpectedly conflicted.");
        }
        return result;
    }

    [Benchmark]
    public string[] ArrayRebase()
    {
        if (
            !SparseFragmentRuntime.TryRebaseSequenceSetUnionArray(
                _before,
                _desired,
                _current,
                _comparer,
                out var result,
                out _
            )
        )
        {
            throw new InvalidOperationException("Array union replay unexpectedly conflicted.");
        }
        return result;
    }
}
