using BenchmarkDotNet.Attributes;
using SparseFragments;

/// <summary>Measures replay when every local dictionary value changes.</summary>
[MemoryDiagnoser]
public class DictionaryDenseRebaseBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool CustomComparer { get; set; }

    [Params(false, true)]
    public bool ConflictingEdits { get; set; }

    private Optional<BenchScalarDictHolder.Fragment?> _scalarBefore;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarCurrent;
    private BenchScalarDictHolder.Patch _scalarLocal = null!;
    private Optional<BenchValueDictionaryHolder.Fragment?> _valueBefore;
    private Optional<BenchValueDictionaryHolder.Fragment?> _valueCurrent;
    private BenchValueDictionaryHolder.Patch _valueLocal = null!;

    [GlobalSetup]
    public void Setup()
    {
        IEqualityComparer<string> comparer = CustomComparer
            ? StringComparer.OrdinalIgnoreCase
            : EqualityComparer<string>.Default;
        var before = new Dictionary<string, int>(Size, comparer);
        var desired = new Dictionary<string, int>(Size, comparer);
        var current = new Dictionary<string, int>(Size + 1, comparer);
        for (var index = 0; index < Size; index++)
        {
            var key = "key-" + index;
            before[key] = index;
            desired[key] = -index - 1;
            current[key] = ConflictingEdits && index % 2 == 0 ? index + Size : index;
        }
        current["remote"] = 99;
        _scalarBefore = ScalarState(before);
        _scalarCurrent = ScalarState(current);
        _scalarLocal = BenchScalarDictHolder.Patch.Between(_scalarBefore, ScalarState(desired));
        _valueBefore = ValueState(before, comparer);
        _valueCurrent = ValueState(current, comparer);
        _valueLocal = BenchValueDictionaryHolder.Patch.Between(
            _valueBefore,
            ValueState(desired, comparer)
        );
        var scalar = Scalar();
        var value = Structural();
        var scalarActual = scalar.Patch.Apply(_scalarCurrent).Value!.Scores.Value!;
        var valueActual = value.Patch.Apply(_valueCurrent).Value!.Values.Value!;
        var conflictCount = ConflictingEdits ? Size / 2 : 0;
        if (
            scalar.Conflicts.Count != conflictCount
            || value.Conflicts.Count != conflictCount
            || scalarActual.Count != Size + 1
            || valueActual.Count != Size + 1
            || scalarActual["remote"] != 99
            || valueActual["remote"].Count != 99
        )
        {
            throw new InvalidOperationException(
                "Dense Rebase must retain remote entries and report conflicts."
            );
        }
        for (var index = 0; index < Size; index++)
        {
            var key = "key-" + index;
            var conflicting = ConflictingEdits && index % 2 == 0;
            var expected = conflicting ? current[key] : desired[key];
            if (
                scalarActual[key] != expected
                || valueActual[key].Count != expected
                || valueActual[key].Id != index
                || _scalarBefore.Value!.Scores.Value![key] != index
                || _valueBefore.Value!.Values.Value![key].Count != index
                || _scalarCurrent.Value!.Scores.Value![key] != current[key]
                || _valueCurrent.Value!.Values.Value![key].Count != current[key]
                || (
                    conflicting
                    && (
                        scalar.Conflicts[index / 2].Path[1] != key
                        || value.Conflicts[index / 2].Path[1] != key
                    )
                )
            )
            {
                throw new InvalidOperationException(
                    "Dense Rebase must preserve values, inputs, and conflict order."
                );
            }
        }
    }

    private static Optional<BenchScalarDictHolder.Fragment?> ScalarState(
        Dictionary<string, int> values
    ) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            new BenchScalarDictHolder.Fragment
            {
                Scores = Optional<Dictionary<string, int>>.Present(values),
            }
        );

    private Optional<BenchValueDictionaryHolder.Fragment?> ValueState(
        Dictionary<string, int> values,
        IEqualityComparer<string> comparer
    ) =>
        Optional<BenchValueDictionaryHolder.Fragment?>.Present(
            new BenchValueDictionaryHolder.Fragment
            {
                Values = Optional<Dictionary<string, BenchValueKeyedItem>>.Present(
                    values.ToDictionary(
                        entry => entry.Key,
                        entry => new BenchValueKeyedItem
                        {
                            Id = entry.Key == "remote" ? Size : int.Parse(entry.Key.AsSpan(4)),
                            Count = entry.Value,
                        },
                        comparer
                    )
                ),
            }
        );

    [Benchmark]
    public RebaseResult<BenchScalarDictHolder.Patch> Scalar() =>
        BenchScalarDictHolder.Patch.Rebase(_scalarBefore, _scalarLocal, _scalarCurrent);

    [Benchmark]
    public RebaseResult<BenchValueDictionaryHolder.Patch> Structural() =>
        BenchValueDictionaryHolder.Patch.Rebase(_valueBefore, _valueLocal, _valueCurrent);
}
