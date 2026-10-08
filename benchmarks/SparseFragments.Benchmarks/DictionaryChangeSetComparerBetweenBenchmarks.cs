using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryChangeSetInputComparer
{
    Default,
    Ordinal,
    IgnoreCase,
    CustomIgnoreCase,
    DerivedDefault,
}

/// <summary>Measures input map normalization and validates comparer-sensitive key changes.</summary>
[MemoryDiagnoser]
public class DictionaryChangeSetComparerBetweenBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetInputComparer Comparer { get; set; }

    [Params(false, true)]
    public bool Edit { get; set; }

    private Optional<BenchScalarDictHolder.Fragment?> _scalarBefore;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarAfter;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralBefore;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralAfter;

    [GlobalSetup]
    public void Setup()
    {
        _scalarBefore = ScalarState(false);
        _scalarAfter = ScalarState(true);
        _structuralBefore = StructuralState(false);
        _structuralAfter = StructuralState(true);
        var scalar = ScalarBetween();
        var structural = StructuralBetween();
        var key = "key-" + (Size - 1);
        if (
            scalar.IsEmpty == Edit
            || structural.IsEmpty == Edit
            || !BenchScalarDictHolder
                .Patch.Between(scalar.ToPatch().Apply(_scalarBefore), _scalarAfter)
                .IsEmpty
            || !BenchScalarDictHolder
                .Patch.Between(scalar.Invert().ToPatch().Apply(_scalarAfter), _scalarBefore)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(structural.ToPatch().Apply(_structuralBefore), _structuralAfter)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(
                    structural.Invert().ToPatch().Apply(_structuralAfter),
                    _structuralBefore
                )
                .IsEmpty
            || _scalarBefore.Value!.Scores.Value!.Count != Size
            || _scalarBefore.Value.Scores.Value[key] != Size - 1
            || _scalarAfter.Value!.Scores.Value![key] != (Edit ? -1 : Size - 1)
            || _structuralBefore.Value!.Servers.Value!.Count != Size
            || _structuralBefore.Value.Servers.Value[key].Count != Size - 1
            || _structuralAfter.Value!.Servers.Value![key].Count != (Edit ? -1 : Size - 1)
        )
            throw new InvalidOperationException(
                "Dictionary differences must preserve endpoints and keep source maps and elements unchanged."
            );

        // ChangeSet keys use the declared default comparer, even when input maps ignore case.
        // Renaming 'key' to 'KEY' must remove the old key and add the new key.
        var beforeMap = NewMap<int>(1);
        beforeMap.Add("key", 1);
        var afterMap = NewMap<int>(1);
        afterMap.Add("KEY", 2);
        var before = ScalarFragment(beforeMap);
        var after = ScalarFragment(afterMap);
        var change = BenchScalarDictHolder.ChangeSet.Between(before, after);
        var actual = change.ToPatch().Apply(before).Value!.Scores.Value!;
        var inverse = change.Invert().ToPatch().Apply(after).Value!.Scores.Value!;
        if (
            change.IsEmpty
            || actual.Count != 1
            || actual.Keys.Single() != "KEY"
            || actual["KEY"] != 2
            || inverse.Count != 1
            || inverse.Keys.Single() != "key"
            || inverse["key"] != 1
            || beforeMap.Keys.Single() != "key"
            || beforeMap["key"] != 1
            || afterMap.Keys.Single() != "KEY"
            || afterMap["KEY"] != 2
        )
            throw new InvalidOperationException(
                "Input comparers must not change declared key semantics or mutate source maps."
            );
    }

    private Dictionary<string, TValue> NewMap<TValue>(int capacity)
    {
        IEqualityComparer<string> comparer = Comparer switch
        {
            DictionaryChangeSetInputComparer.Ordinal => StringComparer.Ordinal,
            DictionaryChangeSetInputComparer.IgnoreCase => StringComparer.OrdinalIgnoreCase,
            DictionaryChangeSetInputComparer.CustomIgnoreCase => CustomIgnoreCaseComparer.Instance,
            _ => EqualityComparer<string>.Default,
        };
        return Comparer == DictionaryChangeSetInputComparer.DerivedDefault
            ? new DerivedMap<TValue>(capacity, comparer)
            : new Dictionary<string, TValue>(capacity, comparer);
    }

    private Optional<BenchScalarDictHolder.Fragment?> ScalarState(bool after)
    {
        var values = NewMap<int>(Size);
        for (var index = 0; index < Size; index++)
            values.Add("key-" + index, after && Edit && index == Size - 1 ? -1 : index);
        return ScalarFragment(values);
    }

    private static Optional<BenchScalarDictHolder.Fragment?> ScalarFragment(
        Dictionary<string, int> values
    ) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            new() { Scores = Optional<Dictionary<string, int>>.Present(values) }
        );

    private Optional<BenchStructuralDictHolder.Fragment?> StructuralState(bool after)
    {
        var values = NewMap<BenchKeyedServer>(Size);
        for (var index = 0; index < Size; index++)
            values.Add(
                "key-" + index,
                new()
                {
                    Id = "srv-" + index,
                    Name = "server-" + index,
                    Count = after && Edit && index == Size - 1 ? -1 : index,
                }
            );
        return Optional<BenchStructuralDictHolder.Fragment?>.Present(
            new() { Servers = Optional<Dictionary<string, BenchKeyedServer>>.Present(values) }
        );
    }

    private sealed class CustomIgnoreCaseComparer : IEqualityComparer<string>
    {
        internal static readonly CustomIgnoreCaseComparer Instance = new();

        public bool Equals(string? left, string? right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);

        public int GetHashCode(string value) => StringComparer.OrdinalIgnoreCase.GetHashCode(value);
    }

    private sealed class DerivedMap<TValue> : Dictionary<string, TValue>
    {
        internal DerivedMap(int capacity, IEqualityComparer<string> comparer)
            : base(capacity, comparer) { }
    }

    [Benchmark]
    public BenchScalarDictHolder.ChangeSet ScalarBetween() =>
        BenchScalarDictHolder.ChangeSet.Between(_scalarBefore, _scalarAfter);

    [Benchmark]
    public BenchStructuralDictHolder.ChangeSet StructuralBetween() =>
        BenchStructuralDictHolder.ChangeSet.Between(_structuralBefore, _structuralAfter);
}
