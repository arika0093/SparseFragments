using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchEqualityDictHolder
{
    public Dictionary<string, int?> NullableValues { get; set; } = new();
    public Dictionary<string, string?> Text { get; set; } = new();
    public Dictionary<string, double> Numbers { get; set; } = new();
    public Dictionary<string, MergeMode> Modes { get; set; } = new();
    public Dictionary<string, BenchCustomScalar> CustomValues { get; set; } = new();
    public Dictionary<string, BenchEnumerableScalar> Sequences { get; set; } = new();
}

/// <summary>Exercises generated equality for nulls, NaN, enums, and nonstandard structs.</summary>
[MemoryDiagnoser]
public class DictionaryChangeSetEqualityComposeBenchmarks
{
    [Params(false, true)]
    public bool Restore { get; set; }

    private BenchEqualityDictHolder.ChangeSet _first = null!;
    private BenchEqualityDictHolder.ChangeSet _second = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = State(0);
        var middle = State(1);
        var after = State(Restore ? 0 : 2);
        _first = BenchEqualityDictHolder.ChangeSet.Between(before, middle);
        _second = BenchEqualityDictHolder.ChangeSet.Between(middle, after);
        var result = Compose();
        if (
            _first.IsEmpty
            || _second.IsEmpty
            || result.IsEmpty != Restore
            || !BenchEqualityDictHolder.Patch.Between(result.ToPatch().Apply(before), after).IsEmpty
            || !BenchEqualityDictHolder
                .Patch.Between(result.Invert().ToPatch().Apply(after), before)
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Dictionary compositions must retain null, NaN, enum, object equality, and enumerable struct semantics."
            );

        // The custom scalar deliberately implements different object and typed equality.
        // Values 1 and 11 must remain distinct under the generated sparse semantics.
        var customBefore = Optional<BenchEqualityDictHolder.Fragment?>.Present(
            BenchEqualityDictHolder.Fragment.From(
                new() { CustomValues = new() { ["custom"] = new BenchCustomScalar(1) } }
            )
        );
        var customMiddle = Optional<BenchEqualityDictHolder.Fragment?>.Present(
            BenchEqualityDictHolder.Fragment.From(
                new() { CustomValues = new() { ["custom"] = new BenchCustomScalar(2) } }
            )
        );
        var customAfter = Optional<BenchEqualityDictHolder.Fragment?>.Present(
            BenchEqualityDictHolder.Fragment.From(
                new() { CustomValues = new() { ["custom"] = new BenchCustomScalar(11) } }
            )
        );
        if (
            BenchEqualityDictHolder
                .ChangeSet.Between(customBefore, customMiddle)
                .Compose(BenchEqualityDictHolder.ChangeSet.Between(customMiddle, customAfter))
                .IsEmpty
        )
            throw new InvalidOperationException("Custom structs must retain object equality.");
    }

    private static Optional<BenchEqualityDictHolder.Fragment?> State(int step) =>
        Optional<BenchEqualityDictHolder.Fragment?>.Present(
            BenchEqualityDictHolder.Fragment.From(
                new()
                {
                    NullableValues = Enumerable
                        .Range(0, 16)
                        .ToDictionary(
                            index => "key-" + index,
                            index => step == 0 ? (int?)null : step
                        ),
                    Text = Enumerable
                        .Range(0, 16)
                        .ToDictionary(
                            index => "key-" + index,
                            index => step == 0 ? null : "text-" + step
                        ),
                    Numbers = Enumerable
                        .Range(0, 16)
                        .ToDictionary(
                            index => "key-" + index,
                            index => step == 0 ? double.NaN : step
                        ),
                    Modes = Enumerable
                        .Range(0, 16)
                        .ToDictionary(
                            index => "key-" + index,
                            index => step == 1 ? MergeMode.SetUnion : MergeMode.Append
                        ),
                    CustomValues = Enumerable
                        .Range(0, 16)
                        .ToDictionary(
                            index => "key-" + index,
                            index => new BenchCustomScalar(step + 1)
                        ),
                    Sequences = Enumerable
                        .Range(0, 16)
                        .ToDictionary(
                            index => "key-" + index,
                            index => new BenchEnumerableScalar(step + 1)
                        ),
                }
            )
        );

    [Benchmark]
    public BenchEqualityDictHolder.ChangeSet Compose() => _first.Compose(_second);
}
