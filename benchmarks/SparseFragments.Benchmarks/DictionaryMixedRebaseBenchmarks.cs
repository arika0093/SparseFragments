using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchValueDictionaryHolder
{
    public Dictionary<string, BenchValueKeyedItem> Values { get; set; } = new();
}

/// <summary>Checks mixed dictionary edits, comparer behavior, and conflict ordering.</summary>
[MemoryDiagnoser]
public class DictionaryMixedRebaseBenchmarks
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
        for (var index = 0; index < Size; index++)
        {
            before["key-" + index] = index;
        }
        var desired = new Dictionary<string, int>(before, comparer);
        desired.Remove("key-0");
        desired["key-1"] = -1;
        desired["local"] = -4;
        desired["shared"] = -5;
        var current = new Dictionary<string, int>(before, comparer)
        {
            ["shared"] = -6,
            ["remote"] = 99,
        };
        if (ConflictingEdits)
        {
            current["key-0"] = 42;
            current["key-1"] = 99;
        }
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
        var expected = new Dictionary<string, int>(current, comparer) { ["local"] = -4 };
        if (!ConflictingEdits)
        {
            expected.Remove("key-0");
            expected["key-1"] = -1;
        }
        string[] conflictKeys = ConflictingEdits ? ["key-0", "key-1", "shared"] : ["shared"];
        if (
            scalarActual.Count != expected.Count
            || valueActual.Count != expected.Count
            || scalar.Conflicts.Count != conflictKeys.Length
            || value.Conflicts.Count != conflictKeys.Length
        )
        {
            throw new InvalidOperationException(
                "Mixed dictionary Rebase must retain independent changes and conflicts."
            );
        }
        foreach (var entry in expected)
        {
            if (
                scalarActual[entry.Key] != entry.Value
                || valueActual[entry.Key].Count != entry.Value
                || valueActual[entry.Key].Id != Id(entry.Key)
            )
            {
                throw new InvalidOperationException(
                    "Mixed dictionary Rebase must retain each entry's values."
                );
            }
        }
        for (var index = 0; index < conflictKeys.Length; index++)
        {
            if (
                scalar.Conflicts[index].Path[1] != conflictKeys[index]
                || value.Conflicts[index].Path[1] != conflictKeys[index]
            )
            {
                throw new InvalidOperationException(
                    "Mixed dictionary Rebase must retain conflict order and key paths."
                );
            }
        }
        if (
            _scalarBefore.Value!.Scores.Value!["key-0"] != 0
            || _valueBefore.Value!.Values.Value!["key-0"].Count != 0
            || _scalarCurrent.Value!.Scores.Value!["key-1"] != current["key-1"]
            || _valueCurrent.Value!.Values.Value!["key-1"].Count != current["key-1"]
        )
        {
            throw new InvalidOperationException(
                "Mixed dictionary Rebase must preserve its input states."
            );
        }
    }

    private int Id(string key) =>
        key switch
        {
            "local" => Size,
            "shared" => Size + 1,
            "remote" => Size + 2,
            _ => int.Parse(key.AsSpan(4)),
        };

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
                            Id = Id(entry.Key),
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
