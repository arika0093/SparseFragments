using System.Collections;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchGeneratedIntegerUnion
{
    [SparseMerge(MergeMode.SetUnion)]
    public List<int> Values { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchGeneratedStringUnion
{
    [SparseMerge(MergeMode.SetUnion)]
    public string?[] Values { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchGeneratedNullableUnion
{
    [SparseMerge(MergeMode.SetUnion)]
    public IReadOnlyList<double?> Values { get; set; } = [];
}

public enum GeneratedUnionScenario
{
    Addition,
    AlreadyApplied,
    RemovalConflict,
}

public readonly struct GeneratedUnionEqualityValue(int id, int count)
    : IEquatable<GeneratedUnionEqualityValue>
{
    public int Id { get; } = id;
    public int Count { get; } = count;

    public bool Equals(GeneratedUnionEqualityValue other) => Id == other.Id;

    public override bool Equals(object? other) =>
        other is GeneratedUnionEqualityValue value && Id == value.Id && Count == value.Count;

    public override int GetHashCode() => Id;
}

[SparseFragmentModel]
public partial class BenchGeneratedCustomUnion
{
    [SparseMerge(MergeMode.SetUnion)]
    public List<GeneratedUnionEqualityValue> Values { get; set; } = [];
}

[MemoryDiagnoser]
public class GeneratedSequenceUnionRebaseBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        GeneratedUnionScenario.Addition,
        GeneratedUnionScenario.AlreadyApplied,
        GeneratedUnionScenario.RemovalConflict
    )]
    public GeneratedUnionScenario Scenario { get; set; }

    private Optional<BenchGeneratedIntegerUnion.Fragment?> _integerBefore;
    private Optional<BenchGeneratedIntegerUnion.Fragment?> _integerCurrent;
    private BenchGeneratedIntegerUnion.Patch _integerLocal = null!;
    private Optional<BenchGeneratedStringUnion.Fragment?> _stringBefore;
    private Optional<BenchGeneratedStringUnion.Fragment?> _stringCurrent;
    private BenchGeneratedStringUnion.Patch _stringLocal = null!;
    private Optional<BenchGeneratedNullableUnion.Fragment?> _nullableBefore;
    private Optional<BenchGeneratedNullableUnion.Fragment?> _nullableCurrent;
    private BenchGeneratedNullableUnion.Patch _nullableLocal = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Enumerable.Range(0, Size).ToList();
        var desired =
            Scenario == GeneratedUnionScenario.RemovalConflict
                ? before.Skip(1).ToList()
                : before.Concat([-1, -1]).ToList();
        var current =
            Scenario == GeneratedUnionScenario.AlreadyApplied
                ? desired.ToList()
                : before.Concat([-2]).ToList();
        var expected =
            Scenario == GeneratedUnionScenario.Addition ? current.Concat([-1]).ToList() : current;
        _integerBefore = IntegerState(before);
        _integerCurrent = IntegerState(current);
        _integerLocal = new BenchGeneratedIntegerUnion.Patch { Values = desired };
        _stringBefore = StringState(before);
        _stringCurrent = StringState(current);
        _stringLocal = new BenchGeneratedStringUnion.Patch { Values = Strings(desired) };
        _nullableBefore = NullableState(before);
        _nullableCurrent = NullableState(current);
        _nullableLocal = new BenchGeneratedNullableUnion.Patch { Values = NullableValues(desired) };
        var integer = IntegerList();
        var text = StringArray();
        var nullable = NullableReadOnlyList();
        var conflicts = Scenario == GeneratedUnionScenario.RemovalConflict ? 1 : 0;
        if (
            integer.Conflicts.Count != conflicts
            || text.Conflicts.Count != conflicts
            || nullable.Conflicts.Count != conflicts
            || !integer.Patch.Apply(_integerCurrent).Value!.Values.Value!.SequenceEqual(expected)
            || !text
                .Patch.Apply(_stringCurrent)
                .Value!.Values.Value!.SequenceEqual(Strings(expected))
            || !nullable
                .Patch.Apply(_nullableCurrent)
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
                "Generated sequence union must preserve values, duplicate order, conflicts and inputs."
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

    private static Optional<BenchGeneratedIntegerUnion.Fragment?> IntegerState(List<int> values) =>
        Optional<BenchGeneratedIntegerUnion.Fragment?>.Present(
            new BenchGeneratedIntegerUnion.Fragment { Values = Optional<List<int>>.Present(values) }
        );

    private static Optional<BenchGeneratedStringUnion.Fragment?> StringState(
        IEnumerable<int> values
    ) =>
        Optional<BenchGeneratedStringUnion.Fragment?>.Present(
            new BenchGeneratedStringUnion.Fragment
            {
                Values = Optional<string?[]>.Present(Strings(values)),
            }
        );

    private static Optional<BenchGeneratedNullableUnion.Fragment?> NullableState(
        IEnumerable<int> values
    ) =>
        Optional<BenchGeneratedNullableUnion.Fragment?>.Present(
            new BenchGeneratedNullableUnion.Fragment
            {
                Values = Optional<IReadOnlyList<double?>>.Present(NullableValues(values)),
            }
        );

    private static void CheckDerivedCollection()
    {
        var before = IntegerState(new ReverseList { 1, 2 });
        var current = IntegerState(new ReverseList { 1, 2, 4 });
        var local = new BenchGeneratedIntegerUnion.Patch
        {
            Values = new ReverseList { 1, 2, 3 },
        };
        var result = BenchGeneratedIntegerUnion.Patch.Rebase(before, local, current);
        if (
            result.HasConflicts
            || !result.Patch.Apply(current).Value!.Values.Value!.SequenceEqual([4, 2, 1, 3])
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
        var original = new GeneratedUnionEqualityValue(1, 0);
        var changed = new GeneratedUnionEqualityValue(1, 1);
        var before = Optional<BenchGeneratedCustomUnion.Fragment?>.Present(
            new BenchGeneratedCustomUnion.Fragment
            {
                Values = Optional<List<GeneratedUnionEqualityValue>>.Present([original]),
            }
        );
        var current = Optional<BenchGeneratedCustomUnion.Fragment?>.Present(
            new BenchGeneratedCustomUnion.Fragment
            {
                Values = Optional<List<GeneratedUnionEqualityValue>>.Present([original, new(2, 0)]),
            }
        );
        var local = new BenchGeneratedCustomUnion.Patch
        {
            Values = new List<GeneratedUnionEqualityValue> { changed },
        };
        var result = BenchGeneratedCustomUnion.Patch.Rebase(before, local, current);
        if (
            result.Conflicts.Count != 1
            || result.Patch.Apply(current).Value!.Values.Value![0].Count != 0
        )
        {
            throw new InvalidOperationException(
                "Custom values must retain object equality instead of IEquatable equality."
            );
        }
    }

    [Benchmark]
    public RebaseResult<BenchGeneratedIntegerUnion.Patch> IntegerList() =>
        BenchGeneratedIntegerUnion.Patch.Rebase(_integerBefore, _integerLocal, _integerCurrent);

    [Benchmark]
    public RebaseResult<BenchGeneratedStringUnion.Patch> StringArray() =>
        BenchGeneratedStringUnion.Patch.Rebase(_stringBefore, _stringLocal, _stringCurrent);

    [Benchmark]
    public RebaseResult<BenchGeneratedNullableUnion.Patch> NullableReadOnlyList() =>
        BenchGeneratedNullableUnion.Patch.Rebase(_nullableBefore, _nullableLocal, _nullableCurrent);
}
