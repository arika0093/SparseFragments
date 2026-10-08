using SparseFragments.CompilerServices;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class SequenceSetSettings
{
    [SparseMerge(MergeMode.SetUnion)]
    public IReadOnlyList<string> Values { get; set; } = [];
}

/// <summary>
/// Scale and semantics tests for issue #59: the typed sequence rebase helpers must
/// agree with the boxed <c>object?</c> + delegate path on success, rebased content,
/// and conflicts across no-op, add, removal, concurrent, conflict, duplicate, and
/// many-change layouts, with default and custom comparers.
/// </summary>
public sealed class SequenceRebaseHelperTests
{
    private static readonly Func<object?, object?, bool> BoxedEqual = (left, right) =>
        SparseFragmentRuntime.AreEqual(left, right);

    [Test]
    public void TypedAppendMatchesBoxedAcrossRandomLayouts()
    {
        var random = new Random(5901);
        for (var trial = 0; trial < 200; trial++)
        {
            var (before, desired, current) = RandomAppendLayout(random);

            var boxedOk = SparseFragmentRuntime.TryRebaseAppend(
                Box(before),
                Box(desired),
                Box(current),
                BoxedEqual,
                out var boxedRebased,
                out var boxedReason
            );
            var typedOk = SparseFragmentRuntime.TryRebaseSequenceAppend(
                before,
                desired,
                current,
                null,
                out var typedRebased,
                out var typedReason
            );

            typedOk.ShouldBe(boxedOk);
            (typedReason is null).ShouldBe(boxedReason is null);
            if (boxedOk)
            {
                typedRebased.ShouldBe(boxedRebased.Select(v => (string?)v).ToList());
            }
        }
    }

    [Test]
    public void TypedSetUnionMatchesBoxedAcrossRandomLayouts()
    {
        var random = new Random(5959);
        for (var trial = 0; trial < 200; trial++)
        {
            var (before, desired, current) = RandomSetUnionLayout(random);

            var boxedOk = SparseFragmentRuntime.TryRebaseSetUnion(
                Box(before),
                Box(desired),
                Box(current),
                BoxedEqual,
                out var boxedRebased,
                out var boxedReason
            );
            var typedOk = SparseFragmentRuntime.TryRebaseSequenceSetUnion(
                before,
                desired,
                current,
                null,
                out var typedRebased,
                out var typedReason
            );

            typedOk.ShouldBe(boxedOk);
            (typedReason is null).ShouldBe(boxedReason is null);
            if (boxedOk)
            {
                typedRebased.ShouldBe(boxedRebased.Select(v => (string?)v).ToList());
            }
        }
    }

    [Test]
    public void TypedSetUnionRespectsCustomComparer()
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var before = new List<string> { "a", "b" };
        // "B" already exists under the comparer, so only "c" is locally added.
        var desired = new List<string> { "a", "B", "c" };
        var current = new List<string> { "a", "b", "D" };

        SparseFragmentRuntime
            .TryRebaseSequenceSetUnion(
                before,
                desired,
                current,
                comparer,
                out var rebased,
                out var reason
            )
            .ShouldBeTrue(reason);
        rebased.Count.ShouldBe(4);
        rebased.ShouldContain("c");
        rebased.ShouldContain("D");

        // Case-only differences are not removals or additions under the comparer.
        SparseFragmentRuntime
            .TryRebaseSequenceSetUnion(
                before,
                new List<string> { "A", "B" },
                new List<string> { "a", "b", "x" },
                comparer,
                out var noopRebased,
                out var noopReason
            )
            .ShouldBeTrue(noopReason);
        noopRebased.ShouldBe(["a", "b", "x"]);

        // A genuine removal still conflicts with a concurrent change.
        SparseFragmentRuntime
            .TryRebaseSequenceSetUnion(
                before,
                new List<string> { "a" },
                current,
                comparer,
                out _,
                out var conflictReason
            )
            .ShouldBeFalse();
        conflictReason.ShouldNotBeNull();
    }

    [Test]
    public void TypedSetUnionDedupsRepeatedLocalAdditions()
    {
        var before = new List<string> { "a" };
        var desired = new List<string> { "a", "b", "b", "c" };
        var current = new List<string> { "a", "x" };

        SparseFragmentRuntime
            .TryRebaseSequenceSetUnion(
                before,
                desired,
                current,
                null,
                out var rebased,
                out var reason
            )
            .ShouldBeTrue(reason);
        rebased.ShouldBe(["a", "x", "b", "c"]);
    }

    [Test]
    public void TypedSetUnionSupportsValueTypes()
    {
        var before = new List<int> { 1, 2, 3 };
        var desired = new List<int> { 1, 2, 3, 4 };
        var current = new List<int> { 1, 2, 3, 5 };

        SparseFragmentRuntime
            .TryRebaseSequenceSetUnion(
                before,
                desired,
                current,
                null,
                out var rebased,
                out var reason
            )
            .ShouldBeTrue(reason);
        rebased.ShouldBe([1, 2, 3, 5, 4]);
    }

    [Test]
    public void TypedHelpersScaleLinearlyOnLargeSequences()
    {
        const int size = 2048;
        var before = Enumerable.Range(0, size).Select(i => $"item-{i}").ToList();
        var desired = new List<string>(before) { "local-new" };
        var current = new List<string>(before) { "concurrent-new" };

        SparseFragmentRuntime
            .TryRebaseSequenceSetUnion(
                before,
                desired,
                current,
                null,
                out var rebased,
                out var reason
            )
            .ShouldBeTrue(reason);
        rebased.Count.ShouldBe(size + 2);
        rebased[^2].ShouldBe("concurrent-new");
        rebased[^1].ShouldBe("local-new");

        SparseFragmentRuntime
            .TryRebaseSequenceAppend(before, desired, current, null, out var appendRebased, out _)
            .ShouldBeTrue();
        appendRebased.ShouldBe([.. current, "local-new"]);
    }

    [Test]
    public void GeneratedSequenceSetUnionRebasesCleanUnion()
    {
        var baseState = FragmentState(["a"]);
        var local = new SequenceSetSettings.Patch
        {
            Values = new List<string> { "a", "b" },
        };
        var currentState = FragmentState(["a", "c"]);

        var result = SequenceSetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var values = Apply(result.Rebased, currentState).Value!.Values.Value!;
        values.ShouldBe(["a", "c", "b"]);
    }

    [Test]
    public void GeneratedSequenceSetUnionReportsRemovalConflict()
    {
        var baseState = FragmentState(["a", "b"]);
        var local = new SequenceSetSettings.Patch { Values = new List<string> { "a" } };
        var currentState = FragmentState(["a", "b", "c"]);

        var result = SequenceSetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.CollectionSetUnion);
        result.Conflicts.Single().Path.ShouldBe(["Values"]);
    }

    [Test]
    public void GeneratedSequenceSetUnionReplaysRemovalOntoCleanState()
    {
        var baseState = FragmentState(["a", "b"]);
        var local = new SequenceSetSettings.Patch { Values = new List<string> { "a" } };

        var result = SequenceSetSettings.Patch.Rebase(baseState, local, baseState);

        result.HasConflicts.ShouldBeFalse();
        Apply(result.Rebased, baseState).Value!.Values.Value!.ShouldBe(["a"]);
    }

    private static (
        List<string?> Before,
        List<string?> Desired,
        List<string?> Current
    ) RandomAppendLayout(Random random)
    {
        var size = random.Next(0, 10);
        var before = Enumerable.Range(0, size).Select(_ => RandomElement(random)).ToList();
        var desired = new List<string?>(before);
        var current = new List<string?>(before);
        switch (random.Next(0, 6))
        {
            case 0:
                break;
            case 1:
                desired.Add(RandomElement(random));
                break;
            case 2:
                if (desired.Count > 0)
                {
                    desired.RemoveAt(random.Next(desired.Count));
                }

                break;
            case 3:
                current.Add(RandomElement(random));
                desired.Add(RandomElement(random));
                break;
            case 4:
                if (current.Count > 0)
                {
                    current[random.Next(current.Count)] = "__changed__";
                }
                else
                {
                    current.Add("__changed__");
                }

                desired.Add(RandomElement(random));
                break;
            default:
                for (var i = 0; i < desired.Count; i++)
                {
                    if (random.Next(0, 2) == 0)
                    {
                        desired[i] = RandomElement(random);
                    }
                }

                break;
        }

        return (before, desired, current);
    }

    private static (
        List<string?> Before,
        List<string?> Desired,
        List<string?> Current
    ) RandomSetUnionLayout(Random random)
    {
        var domain = new[] { "a", "b", "c", "d", null, "a" };
        List<string?> Pick(int count) =>
            Enumerable.Range(0, count).Select(_ => domain[random.Next(domain.Length)]).ToList();

        var before = Pick(random.Next(0, 8));
        var scenario = random.Next(0, 7);
        var desired = new List<string?>(before);
        var current = new List<string?>(before);
        switch (scenario)
        {
            case 0:
                break;
            case 1:
                desired.Add("local-new");
                break;
            case 2:
                if (desired.Count > 0)
                {
                    desired.RemoveAt(random.Next(desired.Count));
                }

                break;
            case 3:
                desired.Add("local-new");
                current.Add("concurrent-new");
                break;
            case 4:
                if (desired.Count > 0)
                {
                    desired.RemoveAt(random.Next(desired.Count));
                }

                current.Add("concurrent-new");
                break;
            case 5:
                desired.Add("local-new");
                desired.Add("local-new");
                current.Add("local-new");
                break;
            default:
                desired = Pick(random.Next(0, 8));
                current = Pick(random.Next(0, 8));
                break;
        }

        return (before, desired, current);
    }

    private static string? RandomElement(Random random)
    {
        var domain = new[] { "a", "b", "c", null, "a" };
        return domain[random.Next(domain.Length)];
    }

    private static List<object?> Box(List<string?> values) =>
        values.Select(v => (object?)v).ToList();

    private static Optional<SequenceSetSettings.Fragment?> FragmentState(
        IEnumerable<string> values
    ) =>
        Optional<SequenceSetSettings.Fragment?>.Present(
            new SequenceSetSettings.Fragment
            {
                Values = Optional<IReadOnlyList<string>>.Present(values.ToList()),
            }
        );

    private static Optional<SequenceSetSettings.Fragment?> Apply(
        SequenceSetSettings.Patch patch,
        Optional<SequenceSetSettings.Fragment?> state
    ) => patch.Apply(state);
}
