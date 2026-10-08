using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

public enum UnionFallbackScenario
{
    Addition,
    AlreadyApplied,
    Removal,
    RemovalConflict,
}

[MemoryDiagnoser]
public class SequenceUnionFallbackBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public UnionFallbackScenario Scenario { get; set; }

    private object?[] _before = null!;
    private object?[] _desired = null!;
    private object?[] _current = null!;
    private Optional<BenchGeneratedCustomUnion.Fragment?> _generatedBefore;
    private Optional<BenchGeneratedCustomUnion.Fragment?> _generatedCurrent;
    private BenchGeneratedCustomUnion.Patch _local = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Enumerable
            .Range(0, Size)
            .Select(index => new GeneratedUnionEqualityValue(index, 0))
            .ToList();
        GeneratedUnionEqualityValue[] additions = [new(-1, 0), new(-1, 1), new(-1, 0)];
        var removal =
            Scenario is UnionFallbackScenario.Removal or UnionFallbackScenario.RemovalConflict;
        var desired = removal ? before.Skip(1).ToList() : before.Concat(additions).ToList();
        var current = Scenario switch
        {
            UnionFallbackScenario.Removal => before.ToList(),
            UnionFallbackScenario.AlreadyApplied => desired.ToList(),
            _ => before.Concat([new GeneratedUnionEqualityValue(-2, 0), before[0]]).ToList(),
        };
        var expected = Scenario switch
        {
            UnionFallbackScenario.Removal => desired,
            UnionFallbackScenario.Addition => current.Concat(additions.Take(2)).ToList(),
            _ => current,
        };
        _before = before.Cast<object?>().ToArray();
        _desired = desired.Cast<object?>().ToArray();
        _current = current.Cast<object?>().ToArray();
        _generatedBefore = State(before);
        _generatedCurrent = State(current);
        _local = new BenchGeneratedCustomUnion.Patch { Values = desired };
        var success = SparseFragmentRuntime.TryRebaseSetUnion(
            _before,
            _desired,
            _current,
            Equal,
            out var actual,
            out var reason
        );
        var generated = GeneratedCustomList();
        var conflict = Scenario == UnionFallbackScenario.RemovalConflict;
        if (
            success == conflict
            || (reason is not null) != conflict
            || generated.Conflicts.Count != (conflict ? 1 : 0)
            || (!conflict && !actual.SequenceEqual(expected.Cast<object?>()))
            || !generated
                .Rebased.Apply(_generatedCurrent)
                .Value!.Values.Value!.Select(value => (value.Id, value.Count))
                .SequenceEqual(expected.Select(value => (value.Id, value.Count)))
            || !_before.SequenceEqual(before.Cast<object?>())
            || !_desired.SequenceEqual(desired.Cast<object?>())
            || !_current.SequenceEqual(current.Cast<object?>())
        )
        {
            throw new InvalidOperationException(
                "Fallback union must preserve object equality, duplicates, order, conflicts and inputs."
            );
        }
        object?[] asymmetricBefore = ["before"];
        object?[] asymmetricDesired = ["desired"];
        if (
            !SparseFragmentRuntime.TryRebaseSetUnion(
                asymmetricBefore,
                asymmetricDesired,
                asymmetricBefore,
                static (candidate, value) =>
                    Equals(candidate, value) || Equals(candidate, "desired"),
                out var asymmetric,
                out _
            ) || !asymmetric.SequenceEqual(["before", "desired"])
        )
        {
            throw new InvalidOperationException(
                "Fallback comparisons must retain their operand order."
            );
        }
        var enumeratedBefore = new EnumerationList();
        if (
            !SparseFragmentRuntime.TryRebaseSetUnion(
                enumeratedBefore,
                ["enumerated", "added"],
                ["enumerated", "concurrent"],
                Equal,
                out var enumeratedResult,
                out _
            ) || !enumeratedResult.SequenceEqual(["enumerated", "concurrent", "added"])
        )
        {
            throw new InvalidOperationException(
                "Custom collections must retain enumeration semantics."
            );
        }
    }

    private sealed class EnumerationList : IReadOnlyList<object?>
    {
        public int Count => 1;
        public object? this[int index] => "indexed";

        public IEnumerator<object?> GetEnumerator() =>
            ((IEnumerable<object?>)new object?[] { "enumerated" }).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static bool Equal(object? candidate, object? value) => Equals(candidate, value);

    private static Optional<BenchGeneratedCustomUnion.Fragment?> State(
        List<GeneratedUnionEqualityValue> values
    ) =>
        Optional<BenchGeneratedCustomUnion.Fragment?>.Present(
            new BenchGeneratedCustomUnion.Fragment
            {
                Values = Optional<List<GeneratedUnionEqualityValue>>.Present(values),
            }
        );

    [Benchmark]
    public IReadOnlyList<object?> Boxed()
    {
        SparseFragmentRuntime.TryRebaseSetUnion(
            _before,
            _desired,
            _current,
            Equal,
            out var result,
            out _
        );
        return result;
    }

    [Benchmark]
    public RebaseResult<BenchGeneratedCustomUnion.Patch> GeneratedCustomList() =>
        BenchGeneratedCustomUnion.Patch.Rebase(_generatedBefore, _local, _generatedCurrent);
}
