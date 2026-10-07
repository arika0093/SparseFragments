using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

/// <summary>Measures replay when unchanged or applied inputs share a collection instance.</summary>
[MemoryDiagnoser]
public class SequenceUnionIdentityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool DesiredIsCurrent { get; set; }

    [Params(false, true)]
    public bool CustomComparer { get; set; }

    private List<string?> _before = null!;
    private List<string?> _desired = null!;
    private List<string?> _current = null!;
    private IEqualityComparer<string?> _comparer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _comparer = CustomComparer ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _before = Enumerable.Range(0, Size).Select(index => (string?)("base-" + index)).ToList();
        _before.Add(null);
        _before.Add(_before[0]);
        if (DesiredIsCurrent)
        {
            _desired = new List<string?>(_before) { "local", "LOCAL", "local" };
            _current = _desired;
        }
        else
        {
            _desired = _before;
            _current = new List<string?>(_before) { "remote", "remote" };
        }
        var beforeCopy = _before.ToArray();
        var desiredCopy = _desired.ToArray();
        var currentCopy = _current.ToArray();
        var list = Rebase();
        var array = ArrayRebase();
        if (
            !list.SequenceEqual(currentCopy)
            || !array.SequenceEqual(currentCopy)
            || ReferenceEquals(list, _current)
            || !_before.SequenceEqual(beforeCopy)
            || !_desired.SequenceEqual(desiredCopy)
            || !_current.SequenceEqual(currentCopy)
        )
            throw new InvalidOperationException(
                "Identity replay must copy the current values without changing inputs or dropping duplicates and nulls."
            );
    }

    [Benchmark]
    public List<string?> Rebase()
    {
        if (
            !SparseFragmentRuntime.TryRebaseSequenceSetUnion(
                _before,
                _desired,
                _current,
                _comparer,
                out var result,
                out var reason
            ) || reason is not null
        )
            throw new InvalidOperationException("Identity replay must succeed.");
        return result;
    }

    [Benchmark]
    public string?[] ArrayRebase()
    {
        if (
            !SparseFragmentRuntime.TryRebaseSequenceSetUnionArray(
                _before,
                _desired,
                _current,
                _comparer,
                out var result,
                out var reason
            ) || reason is not null
        )
            throw new InvalidOperationException("Identity array replay must succeed.");
        return result;
    }
}
