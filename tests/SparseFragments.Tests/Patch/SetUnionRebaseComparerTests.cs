using SparseFragments;
using SparseFragments.CompilerServices;

namespace SparseFragments.Tests;

public sealed class SetUnionRebaseComparerTests
{
    [Test]
    public void ConcurrentAdditionsUnionCaseInsensitively()
    {
        var baseState = FragmentState(IgnoreCaseSet("a"));
        var local = new SetSettings.Patch
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "B" },
        };
        var currentState = FragmentState(IgnoreCaseSet("a", "c"));

        var result = SetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = Apply(result.Rebased, currentState).Value!.Values.Value!;
        applied.Count.ShouldBe(3);
        applied.Contains("b", StringComparer.OrdinalIgnoreCase).ShouldBeTrue();
        applied.Contains("c", StringComparer.OrdinalIgnoreCase).ShouldBeTrue();
        ComparerOf(applied).ShouldBe(StringComparer.OrdinalIgnoreCase);
        SemanticOracle.AssertEqual(
            FragmentState(IgnoreCaseSet("a", "b", "c")),
            Apply(result.Rebased, currentState),
            "rebased case-insensitive additions apply cleanly onto current"
        );
    }

    [Test]
    public void AlreadyAppliedAdditionIsNoopUnderCustomComparer()
    {
        var baseState = FragmentState(IgnoreCaseSet("a"));
        var local = new SetSettings.Patch
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" },
        };
        var currentState = FragmentState(IgnoreCaseSet("A", "B"));

        var result = SetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = Apply(result.Rebased, currentState).Value!.Values.Value!;
        SparseFragmentRuntime.AreSetEqual(applied, currentState.Value!.Values.Value).ShouldBeTrue();
        ComparerOf(applied).ShouldBe(StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void CaseOnlyLocalEditIsNoopUnderCustomComparer()
    {
        var baseState = FragmentState(IgnoreCaseSet("a"));
        var local = new SetSettings.Patch
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" },
        };
        var currentState = FragmentState(IgnoreCaseSet("a", "x"));

        var result = SetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = Apply(result.Rebased, currentState).Value!.Values.Value!;
        SparseFragmentRuntime.AreSetEqual(applied, currentState.Value!.Values.Value).ShouldBeTrue();
    }

    [Test]
    public void RemovalWithoutConcurrentChangePreservesComparer()
    {
        var baseState = FragmentState(IgnoreCaseSet("a", "b"));
        var local = new SetSettings.Patch
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" },
        };
        var currentState = FragmentState(IgnoreCaseSet("a", "b"));

        var result = SetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = Apply(result.Rebased, currentState).Value!.Values.Value!;
        applied.Count.ShouldBe(1);
        applied.Contains("a", StringComparer.OrdinalIgnoreCase).ShouldBeTrue();
        ComparerOf(applied).ShouldBe(StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void RemovalWithConcurrentAdditionConflicts()
    {
        var baseState = FragmentState(IgnoreCaseSet("a", "b"));
        var local = new SetSettings.Patch
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" },
        };
        var currentState = FragmentState(IgnoreCaseSet("a", "b", "c"));

        var result = SetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.CollectionSetUnion);
        result.Conflicts.Single().PathText.ShouldBe("Values");
    }

    [Test]
    public void DifferingComparersWithAdditionsUnionOntoCurrent()
    {
        var baseState = FragmentState(IgnoreCaseSet("a"));
        var local = new SetSettings.Patch
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" },
        };
        var currentState = FragmentState(
            new SetSettings.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(StringComparer.Ordinal) { "a", "c" }
                ),
            }
        );

        var result = SetSettings.Patch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = Apply(result.Rebased, currentState).Value!.Values.Value!;
        applied.Count.ShouldBe(3);
        applied.ShouldContain("a");
        applied.ShouldContain("b");
        applied.ShouldContain("c");
        // The union result is rooted in the current set so concurrent elements survive
        // the comparer change; the current comparer is preserved.
        ComparerOf(applied).ShouldBe(StringComparer.Ordinal);
    }

    [Test]
    public void DifferingComparersWithRemovalConflicts()
    {
        var baseState = FragmentState(IgnoreCaseSet("a", "b"));
        var local = new SetSettings.Patch
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" },
        };
        var currentState = FragmentState(
            new SetSettings.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(StringComparer.Ordinal) { "a", "b" }
                ),
            }
        );

        var result = SetSettings.Patch.Rebase(baseState, local, currentState);

        // The comparer is part of the set value, so the current set counts as
        // concurrently changed even though its elements match the baseline.
        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Single().Kind.ShouldBe(SparseConflictKind.CollectionSetUnion);
        result.Conflicts.Single().PathText.ShouldBe("Values");
    }

    [Test]
    public void TypedHelperUnionsValueTypeSets()
    {
        var before = new HashSet<int> { 1 };
        var desired = new HashSet<int> { 1, 2 };
        var current = new HashSet<int> { 1, 3 };

        SparseFragmentRuntime
            .TryRebaseSetUnion(before, desired, current, out var rebased, out var reason)
            .ShouldBeTrue();
        reason.ShouldBeNull();
        rebased.SetEquals(new[] { 1, 2, 3 }).ShouldBeTrue();
    }

    [Test]
    public void TypedHelperReportsRemovalConflict()
    {
        var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b", "c" };

        SparseFragmentRuntime
            .TryRebaseSetUnion(before, desired, current, out _, out var reason)
            .ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    private static SetSettings.Fragment IgnoreCaseSet(params string[] values) =>
        new()
        {
            Values = Optional<ISet<string>>.Present(
                new HashSet<string>(values, StringComparer.OrdinalIgnoreCase)
            ),
        };

    private static IEqualityComparer<string> ComparerOf(ISet<string> values)
    {
        var hashSet = values as HashSet<string>;
        hashSet.ShouldNotBeNull();
        return hashSet.Comparer;
    }

    private static Optional<SetSettings.Fragment?> FragmentState(SetSettings.Fragment fragment) =>
        Optional<SetSettings.Fragment?>.Present(fragment);

    private static Optional<SetSettings.Fragment?> Apply(
        SetSettings.Patch patch,
        Optional<SetSettings.Fragment?> state
    ) => patch.Apply(state);
}
