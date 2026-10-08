using System.Collections;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchGeneratedIntegerAppend
{
    [SparseMerge(MergeMode.Append)]
    public List<int> Values { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchGeneratedStringAppend
{
    [SparseMerge(MergeMode.Append)]
    public string?[] Values { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchGeneratedNullableAppend
{
    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<double?> Values { get; set; } = [];
}

public enum GeneratedAppendScenario
{
    Addition,
    AlreadyApplied,
    PrefixConflict,
}

public readonly struct GeneratedAppendEqualityValue(int id, int count)
    : IEquatable<GeneratedAppendEqualityValue>
{
    public int Id { get; } = id;
    public int Count { get; } = count;

    public bool Equals(GeneratedAppendEqualityValue other) => Id == other.Id;

    public override bool Equals(object? other) =>
        other is GeneratedAppendEqualityValue value && Id == value.Id && Count == value.Count;

    public override int GetHashCode() => Id;
}

[SparseFragmentModel]
public partial class BenchGeneratedCustomAppend
{
    [SparseMerge(MergeMode.Append)]
    public List<GeneratedAppendEqualityValue> Values { get; set; } = [];
}

[MemoryDiagnoser]
public class GeneratedSequenceAppendRebaseBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        GeneratedAppendScenario.Addition,
        GeneratedAppendScenario.AlreadyApplied,
        GeneratedAppendScenario.PrefixConflict
    )]
    public GeneratedAppendScenario Scenario { get; set; }

    private Optional<BenchGeneratedIntegerAppend.Fragment?> _integerBefore;
    private Optional<BenchGeneratedIntegerAppend.Fragment?> _integerCurrent;
    private BenchGeneratedIntegerAppend.Patch _integerLocal = null!;
    private Optional<BenchGeneratedStringAppend.Fragment?> _stringBefore;
    private Optional<BenchGeneratedStringAppend.Fragment?> _stringCurrent;
    private BenchGeneratedStringAppend.Patch _stringLocal = null!;
    private Optional<BenchGeneratedNullableAppend.Fragment?> _nullableBefore;
    private Optional<BenchGeneratedNullableAppend.Fragment?> _nullableCurrent;
    private BenchGeneratedNullableAppend.Patch _nullableLocal = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Enumerable.Range(0, Size).ToList();
        var desired = before.Concat([-1, -1]).ToList();
        var current =
            Scenario == GeneratedAppendScenario.AlreadyApplied
                ? desired.ToList()
                : before.Concat([-2]).ToList();
        if (Scenario == GeneratedAppendScenario.PrefixConflict)
        {
            current[0] = -2;
        }
        var expected =
            Scenario == GeneratedAppendScenario.Addition
                ? current.Concat([-1, -1]).ToList()
                : current;
        _integerBefore = IntegerState(before);
        _integerCurrent = IntegerState(current);
        _integerLocal = new BenchGeneratedIntegerAppend.Patch { Values = desired };
        _stringBefore = StringState(before);
        _stringCurrent = StringState(current);
        _stringLocal = new BenchGeneratedStringAppend.Patch { Values = Strings(desired) };
        _nullableBefore = NullableState(before);
        _nullableCurrent = NullableState(current);
        _nullableLocal = new BenchGeneratedNullableAppend.Patch
        {
            Values = NullableValues(desired),
        };
        var integer = IntegerList();
        var text = StringArray();
        var nullable = NullableReadOnlyList();
        var conflicts = Scenario == GeneratedAppendScenario.PrefixConflict ? 1 : 0;
        if (
            integer.Conflicts.Count != conflicts
            || text.Conflicts.Count != conflicts
            || nullable.Conflicts.Count != conflicts
            || !integer.Rebased.Apply(_integerCurrent).Value!.Values.Value!.SequenceEqual(expected)
            || !text
                .Rebased.Apply(_stringCurrent)
                .Value!.Values.Value!.SequenceEqual(Strings(expected))
            || !nullable
                .Rebased.Apply(_nullableCurrent)
                .Value!.Values.Value!.SequenceEqual(NullableValues(expected))
            || !_integerBefore.Value!.Values.Value!.SequenceEqual(before)
            || !_integerCurrent.Value!.Values.Value!.SequenceEqual(current)
            || !_stringBefore.Value!.Values.Value!.SequenceEqual(Strings(before))
            || !_stringCurrent.Value!.Values.Value!.SequenceEqual(Strings(current))
            || !_nullableBefore.Value!.Values.Value!.SequenceEqual(NullableValues(before))
            || !_nullableCurrent.Value!.Values.Value!.SequenceEqual(NullableValues(current))
        )
        {
            throw new InvalidOperationException(
                "Generated sequence append must preserve values, duplicate order, conflicts and inputs."
            );
        }
        CheckDerivedCollection();
        CheckCustomEquality();
    }

    private static string?[] Strings(IEnumerable<int> values) =>
        values.Select(value => value == 0 ? null : "value-" + value).ToArray();

    private static List<double?> NullableValues(IEnumerable<int> values) =>
        values
            .Select(value =>
                value == 0 ? (double?)null
                : value == 1 ? double.NaN
                : value
            )
            .ToList();

    private static Optional<BenchGeneratedIntegerAppend.Fragment?> IntegerState(List<int> values) =>
        Optional<BenchGeneratedIntegerAppend.Fragment?>.Present(
            new BenchGeneratedIntegerAppend.Fragment
            {
                Values = Optional<List<int>>.Present(values),
            }
        );

    private static Optional<BenchGeneratedStringAppend.Fragment?> StringState(
        IEnumerable<int> values
    ) =>
        Optional<BenchGeneratedStringAppend.Fragment?>.Present(
            new BenchGeneratedStringAppend.Fragment
            {
                Values = Optional<string?[]>.Present(Strings(values)),
            }
        );

    private static Optional<BenchGeneratedNullableAppend.Fragment?> NullableState(
        IEnumerable<int> values
    ) =>
        Optional<BenchGeneratedNullableAppend.Fragment?>.Present(
            new BenchGeneratedNullableAppend.Fragment
            {
                Values = Optional<IReadOnlyList<double?>>.Present(NullableValues(values)),
            }
        );

    private static void CheckDerivedCollection()
    {
        var before = IntegerState(new ReverseList { 1, 2 });
        var current = IntegerState(new ReverseList { 4, 1, 2 });
        var local = new BenchGeneratedIntegerAppend.Patch
        {
            Values = new ReverseList { 3, 1, 2 },
        };
        var result = BenchGeneratedIntegerAppend.Patch.Rebase(before, local, current);
        if (
            result.HasConflicts
            || !result.Rebased.Apply(current).Value!.Values.Value!.SequenceEqual([2, 1, 4, 3])
        )
        {
            throw new InvalidOperationException(
                "Derived collections must retain their non-generic enumeration semantics."
            );
        }
    }

    private sealed class ReverseList : List<int>, IEnumerable
    {
        IEnumerator IEnumerable.GetEnumerator() => this.AsEnumerable().Reverse().GetEnumerator();
    }

    private static void CheckCustomEquality()
    {
        var original = new GeneratedAppendEqualityValue(1, 0);
        var changed = new GeneratedAppendEqualityValue(1, 1);
        var before = Optional<BenchGeneratedCustomAppend.Fragment?>.Present(
            new BenchGeneratedCustomAppend.Fragment
            {
                Values = Optional<List<GeneratedAppendEqualityValue>>.Present([original]),
            }
        );
        var current = Optional<BenchGeneratedCustomAppend.Fragment?>.Present(
            new BenchGeneratedCustomAppend.Fragment
            {
                Values = Optional<List<GeneratedAppendEqualityValue>>.Present([
                    original,
                    new(2, 0),
                ]),
            }
        );
        var local = new BenchGeneratedCustomAppend.Patch
        {
            Values = new List<GeneratedAppendEqualityValue> { changed },
        };
        var result = BenchGeneratedCustomAppend.Patch.Rebase(before, local, current);
        if (
            result.Conflicts.Count != 1
            || result.Rebased.Apply(current).Value!.Values.Value![0].Count != 0
        )
        {
            throw new InvalidOperationException(
                "Custom values must retain object equality instead of IEquatable equality."
            );
        }
    }

    [Benchmark]
    public RebaseResult<BenchGeneratedIntegerAppend.Patch> IntegerList() =>
        BenchGeneratedIntegerAppend.Patch.Rebase(_integerBefore, _integerLocal, _integerCurrent);

    [Benchmark]
    public RebaseResult<BenchGeneratedStringAppend.Patch> StringArray() =>
        BenchGeneratedStringAppend.Patch.Rebase(_stringBefore, _stringLocal, _stringCurrent);

    [Benchmark]
    public RebaseResult<BenchGeneratedNullableAppend.Patch> NullableReadOnlyList() =>
        BenchGeneratedNullableAppend.Patch.Rebase(
            _nullableBefore,
            _nullableLocal,
            _nullableCurrent
        );
}
