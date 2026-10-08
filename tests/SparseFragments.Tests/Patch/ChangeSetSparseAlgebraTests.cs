using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>ChangeSet sparse-transition Compose/Rebase algebra (issue #97).</summary>
/// <remarks>
/// Proves composition and rebase operate directly on canonical sparse transition state:
/// disjoint paths compose without full-state equality, overlapping paths require semantic
/// contiguity (including nested leaves, keyed stable-key transitions, and Missing/null/value
/// presence), whole-root endpoints compose with memberwise transitions through shared-baseline
/// algebra, and rebase consumes only the retained transition plus the supplied current state
/// (scalar/custom/nested/keyed/Append/SetUnion) with structured per-path conflicts.
/// </remarks>
public sealed class ChangeSetSparseAlgebraTests
{
    private static Optional<Settings.Fragment?> Present(Settings.Fragment fragment) =>
        Optional<Settings.Fragment?>.Present(fragment);

    private static Settings.Fragment State(string? label, int retry) =>
        new()
        {
            Label = Optional<string?>.Present(label),
            RetryCount = Optional<int>.Present(retry),
        };

    private static Settings.Fragment NestState(string host, int port) =>
        new()
        {
            Nested = Optional<Nested.Fragment?>.Present(
                new Nested.Fragment
                {
                    Host = Optional<string>.Present(host),
                    Port = Optional<int>.Present(port),
                }
            ),
        };

    private static KeyedServer Srv(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Count = 1,
        };

    private static Optional<KeyedServerHolder.Fragment?> KState(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
        );

    private static void AssertSameSettings(
        Optional<Settings.Fragment?> left,
        Optional<Settings.Fragment?> right
    )
    {
        Settings.Patch.Between(left, right).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DisjointRootMembersComposeFromDifferingSnapshots()
    {
        // First changes Label only; second was produced from an unrelated Label snapshot
        // and changes RetryCount only. Unrelated state must not prevent composition.
        var first = Settings.ChangeSet.Between(
            Present(State("Alice", 1)),
            Present(State("Bob", 1))
        );
        var second = Settings.ChangeSet.Between(
            Present(State("unrelated", 20)),
            Present(State("unrelated", 21))
        );

        var composed = first.Compose(second);

        composed.Label.IsChanged.ShouldBeTrue();
        composed.Label.Before.Value.ShouldBe("Alice");
        composed.Label.After.Value.ShouldBe("Bob");
        composed.RetryCount.IsChanged.ShouldBeTrue();
        composed.RetryCount.Before.Value.ShouldBe(20);
        composed.RetryCount.After.Value.ShouldBe(21);

        AssertSameSettings(
            composed.ToPatch().Apply(Present(State("Alice", 20))),
            Present(State("Bob", 21))
        );
    }

    [Test]
    public void DisjointNestedLeavesComposeUnderSharedParent()
    {
        // Host and Port leaves share the Nested parent but change independently and were
        // produced from differing Host snapshots. No whole-Database equality is required.
        var first = Settings.ChangeSet.Between(
            Present(NestState("a", 1)),
            Present(NestState("b", 1))
        );
        var second = Settings.ChangeSet.Between(
            Present(NestState("unrelated", 1)),
            Present(NestState("unrelated", 2))
        );

        var composed = first.Compose(second);

        composed.Nested.IsEmpty.ShouldBeFalse();
        composed.Nested.Host.IsChanged.ShouldBeTrue();
        composed.Nested.Host.Before.Value.ShouldBe("a");
        composed.Nested.Host.After.Value.ShouldBe("b");
        composed.Nested.Port.IsChanged.ShouldBeTrue();
        composed.Nested.Port.Before.Value.ShouldBe(1);
        composed.Nested.Port.After.Value.ShouldBe(2);

        AssertSameSettings(
            composed.ToPatch().Apply(Present(NestState("a", 1))),
            Present(NestState("b", 2))
        );
    }

    [Test]
    public void ContiguousOverlappingScalarCompose()
    {
        var first = Settings.ChangeSet.Between(
            Present(State("Alice", 1)),
            Present(State("Bob", 1))
        );
        var second = Settings.ChangeSet.Between(
            Present(State("Bob", 1)),
            Present(State("Carol", 1))
        );

        var composed = first.Compose(second);

        composed.Label.IsChanged.ShouldBeTrue();
        composed.Label.Before.Value.ShouldBe("Alice");
        composed.Label.After.Value.ShouldBe("Carol");
        composed.RetryCount.IsChanged.ShouldBeFalse();
        AssertSameSettings(
            composed.ToPatch().Apply(Present(State("Alice", 1))),
            Present(State("Carol", 1))
        );
    }

    [Test]
    public void RoundTripComposeNormalizesToNoOp()
    {
        var first = Settings.ChangeSet.Between(
            Present(State("Alice", 1)),
            Present(State("Bob", 1))
        );
        var second = Settings.ChangeSet.Between(
            Present(State("Bob", 1)),
            Present(State("Alice", 1))
        );

        first.Compose(second).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NonContiguousOverlappingScalarThrows()
    {
        var first = Settings.ChangeSet.Between(
            Present(State("Alice", 1)),
            Present(State("Bob", 1))
        );
        var broken = Settings.ChangeSet.Between(
            Present(State("Dave", 9)),
            Present(State("Carol", 9))
        );

        Should.Throw<InvalidOperationException>(() => first.Compose(broken));
        Should.Throw<InvalidOperationException>(() => Settings.ChangeSet.Compose(first, broken));
    }

    [Test]
    public void MissingNullValueContinuity()
    {
        // Missing -> "x" then "x" -> Missing collapses back to no change on that path.
        var missingBefore = Present(
            new Settings.Fragment { RetryCount = Optional<int>.Present(3) }
        );
        var set = Present(State("x", 3));
        var create = Settings.ChangeSet.Between(missingBefore, set);
        var remove = Settings.ChangeSet.Between(set, missingBefore);
        create.Compose(remove).IsEmpty.ShouldBeTrue();

        // Value -> null -> value is contiguous through the null intermediate.
        var valued = Present(State("a", 3));
        var nulled = Present(State(null, 3));
        var renewed = Present(State("b", 3));
        var toNull = Settings.ChangeSet.Between(valued, nulled);
        var fromNull = Settings.ChangeSet.Between(nulled, renewed);
        var joined = toNull.Compose(fromNull);
        joined.Label.IsChanged.ShouldBeTrue();
        joined.Label.Before.Value.ShouldBe("a");
        joined.Label.After.Value.ShouldBe("b");

        // A mismatched null-adjacent intermediate is non-contiguous.
        var otherNull = Settings.ChangeSet.Between(Present(State("other", 3)), renewed);
        Should.Throw<InvalidOperationException>(() => toNull.Compose(otherNull));
    }

    [Test]
    public void KeyedAddRemoveEditCompose()
    {
        var s0 = KState(Srv("a", "A"), Srv("b", "B"));
        var s1 = KState(Srv("b", "B"), Srv("c", "C"));
        var s2 = KState(Srv("b", "B2"), Srv("c", "C"));
        var first = KeyedServerHolder.ChangeSet.Between(s0, s1);
        var second = KeyedServerHolder.ChangeSet.Between(s1, s2);

        var composed = first.Compose(second);
        var canonical = KeyedServerHolder.ChangeSet.Between(s0, s2);

        composed.Items.Added.Select(e => e.Id).ShouldBe(["c"]);
        composed.Items.Removed.Select(e => e.Id).ShouldBe(["a"]);
        composed.Items.Edited.ContainsKey("b").ShouldBeTrue();
        composed.Items.Edited["b"].Name.After.Value.ShouldBe("B2");
        composed.Items.BeforeOrder.ShouldBe(canonical.Items.BeforeOrder.ToArray());
        composed.Items.AfterOrder.ShouldBe(canonical.Items.AfterOrder.ToArray());
        KeyedServerHolder.Patch.Between(composed.ToPatch().Apply(s0), s2).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedReorderThenEditCompose()
    {
        KeyedServer S(string id) => Srv(id, id);
        var s0 = KState(S("a"), S("b"), S("c"));
        var s1 = KState(S("b"), S("a"), S("c"));
        var s2 = KState(
            S("b"),
            new KeyedServer
            {
                Id = "a",
                Name = "A2",
                Count = 1,
            },
            S("c")
        );
        var first = KeyedServerHolder.ChangeSet.Between(s0, s1);
        var second = KeyedServerHolder.ChangeSet.Between(s1, s2);

        var composed = first.Compose(second);

        composed.Items.OrderChanged.ShouldBeTrue();
        composed.Items.Edited.ContainsKey("a").ShouldBeTrue();
        composed.Items.AfterOrder.ShouldBe(["b", "a", "c"]);
        KeyedServerHolder.Patch.Between(composed.ToPatch().Apply(s0), s2).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NonContiguousKeyedComposeThrows()
    {
        var first = KeyedServerHolder.ChangeSet.Between(
            KState(Srv("a", "A")),
            KState(Srv("a", "A"), Srv("b", "B"))
        );
        var broken = KeyedServerHolder.ChangeSet.Between(
            KState(Srv("a", "A"), Srv("x", "X")),
            KState(Srv("a", "A"))
        );

        Should.Throw<InvalidOperationException>(() => first.Compose(broken));
    }

    [Test]
    public void WholeRootAndMemberwiseComposeThroughSharedBaseline()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var s1 = Present(State("a", 1));
        var s2 = Present(State("b", 1));

        // Whole creation followed by a contiguous memberwise edit.
        var whole = Settings.ChangeSet.Between(missing, s1);
        var memberwise = Settings.ChangeSet.Between(s1, s2);
        var composed = whole.Compose(memberwise);
        composed.IsEmpty.ShouldBeFalse();
        AssertSameSettings(composed.ToPatch().Apply(missing), s2);

        // Memberwise edit followed by whole deletion.
        var deletion = Settings.ChangeSet.Between(s1, missing);
        var first = Settings.ChangeSet.Between(Present(State("zero", 1)), s1);
        var composedDeletion = first.Compose(deletion);
        AssertSameSettings(composedDeletion.ToPatch().Apply(Present(State("zero", 1))), missing);

        // Mismatched shared baseline still throws on the overlapping path.
        var other = Settings.ChangeSet.Between(Present(State("other", 9)), s2);
        Should.Throw<InvalidOperationException>(() => whole.Compose(other));
        var wholeBefore = Settings.ChangeSet.Between(Present(State("zzz", 1)), missing);
        Should.Throw<InvalidOperationException>(() => first.Compose(wholeBefore));
    }

    [Test]
    public void RebaseUnrelatedConcurrentChangeNeedsNoBaseline()
    {
        var changes = Settings.ChangeSet.Between(
            Present(State("Alice", 20)),
            Present(State("Alice", 21))
        );
        var current = Present(State("Bob", 20));

        var result = changes.RebaseOnto(current);

        result.HasConflicts.ShouldBeFalse();
        // The rebased transition is relative to the supplied current state.
        result.Rebased.RetryCount.IsChanged.ShouldBeTrue();
        result.Rebased.RetryCount.Before.Value.ShouldBe(20);
        result.Rebased.RetryCount.After.Value.ShouldBe(21);
        result.Rebased.Label.IsChanged.ShouldBeFalse();
        AssertSameSettings(result.Rebased.ToPatch().Apply(current), Present(State("Bob", 21)));
    }

    [Test]
    public void AlreadyAppliedKeyedRebaseIsNoOp()
    {
        var before = KState(Srv("a", "A"));
        var after = KState(Srv("a", "B"));
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);

        var result = changes.RebaseOnto(after);

        result.HasConflicts.ShouldBeFalse();
        result.Rebased.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ScalarConflictKeepsCleanPath()
    {
        var changes = Settings.ChangeSet.Between(
            Present(State("Alice", 1)),
            Present(State("Bob", 2))
        );
        var current = Present(State("Carol", 1));

        var result = changes.RebaseOnto(current);

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Count.ShouldBe(1);
        result.Conflicts[0].Path.ShouldBe(["Label"]);
        result.Conflicts[0].Kind.ShouldBe(SparseConflictKind.Scalar);
        // The non-conflicting path stays available relative to current.
        result.Rebased.Label.IsChanged.ShouldBeFalse();
        result.Rebased.RetryCount.IsChanged.ShouldBeTrue();
        result.Rebased.RetryCount.After.Value.ShouldBe(2);
        AssertSameSettings(result.Rebased.ToPatch().Apply(current), Present(State("Carol", 2)));
    }

    [Test]
    public void NestedConflictKeepsCleanSibling()
    {
        Optional<Settings.Fragment?> Both(string label, string host) =>
            Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present(label),
                    Nested = Optional<Nested.Fragment?>.Present(
                        new Nested.Fragment { Host = Optional<string>.Present(host) }
                    ),
                }
            );
        var changes = Settings.ChangeSet.Between(Both("L0", "a"), Both("L1", "b"));
        var current = Both("L0", "c");

        var result = changes.RebaseOnto(current);

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Count.ShouldBe(1);
        result.Conflicts[0].Path.ShouldBe(["Nested", "Host"]);
        result.Rebased.Label.IsChanged.ShouldBeTrue();
        result.Rebased.Label.After.Value.ShouldBe("L1");
        AssertSameSettings(result.Rebased.ToPatch().Apply(current), Both("L1", "c"));
    }

    [Test]
    public void KeyedConcurrentAddMergesCleanly()
    {
        var before = KState(Srv("a", "x"), Srv("b", "B"));
        var edited = KState(
            new KeyedServer
            {
                Id = "a",
                Name = "y",
                Count = 1,
            },
            Srv("b", "B")
        );
        var current = KState(Srv("a", "x"), Srv("b", "B"), Srv("c", "C"));
        var changes = KeyedServerHolder.ChangeSet.Between(before, edited);

        var result = changes.RebaseOnto(current);

        result.HasConflicts.ShouldBeFalse();
        var expected = KState(
            new KeyedServer
            {
                Id = "a",
                Name = "y",
                Count = 1,
            },
            Srv("b", "B"),
            Srv("c", "C")
        );
        KeyedServerHolder
            .Patch.Between(result.Rebased.ToPatch().Apply(current), expected)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedEditConflictReported()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            KState(Srv("a", "x")),
            KState(Srv("a", "y"))
        );

        var result = changes.RebaseOnto(KState(Srv("a", "z")));

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void AppendRebaseMergesConcurrentAppends()
    {
        Optional<Settings.Fragment?> PluginsOf(params string[] plugins) =>
            Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("keep"),
                    Plugins = Optional<IReadOnlyList<string>>.Present(plugins.ToList()),
                }
            );
        var changes = Settings.ChangeSet.Between(PluginsOf("p1"), PluginsOf("p1", "p2"));

        var merged = changes.RebaseOnto(PluginsOf("p1", "p3"));

        merged.HasConflicts.ShouldBeFalse();
        merged.Rebased.Plugins.IsChanged.ShouldBeTrue();
        merged.Rebased.Plugins.After.Value.ShouldBe(["p1", "p3", "p2"]);
        Settings
            .Patch.Between(
                merged.Rebased.ToPatch().Apply(PluginsOf("p1", "p3")),
                PluginsOf("p1", "p3", "p2")
            )
            .IsEmpty.ShouldBeTrue();

        var diverged = changes.RebaseOnto(PluginsOf("other"));
        diverged.HasConflicts.ShouldBeTrue();
        diverged.Conflicts[0].Kind.ShouldBe(SparseConflictKind.CollectionAppend);
        diverged.Conflicts[0].Path.ShouldBe(["Plugins"]);
    }

    [Test]
    public void SetUnionRebaseMergesAdds()
    {
        Optional<SetSettings.Fragment?> SetOf(params string[] values) =>
            Optional<SetSettings.Fragment?>.Present(
                SetSettings.Fragment.From(
                    new SetSettings { Values = new HashSet<string>(values, StringComparer.Ordinal) }
                )
            );
        var changes = SetSettings.ChangeSet.Between(SetOf("a"), SetOf("a", "b"));

        var merged = changes.RebaseOnto(SetOf("a", "c"));

        merged.HasConflicts.ShouldBeFalse();
        SetSettings
            .Patch.Between(merged.Rebased.ToPatch().Apply(SetOf("a", "c")), SetOf("a", "c", "b"))
            .IsEmpty.ShouldBeTrue();

        // A removal edit cannot merge beside a concurrent change.
        var removal = SetSettings.ChangeSet.Between(SetOf("a", "b"), SetOf("a"));
        var conflicted = removal.RebaseOnto(SetOf("a", "b", "c"));
        conflicted.HasConflicts.ShouldBeTrue();
        conflicted.Conflicts[0].Kind.ShouldBe(SparseConflictKind.CollectionSetUnion);
    }

    [Test]
    public void CustomStrategyRebaseFromTransition()
    {
        Optional<StrategySettings.Fragment?> ValuesOf(params int[] values) =>
            Optional<StrategySettings.Fragment?>.Present(
                StrategySettings.Fragment.From(new StrategySettings { Values = values.ToList() })
            );
        var changes = StrategySettings.ChangeSet.Between(ValuesOf(1, 2), ValuesOf(3, 4));

        var replayed = changes.RebaseOnto(ValuesOf(1, 2));
        replayed.HasConflicts.ShouldBeFalse();
        replayed.Rebased.Values.IsChanged.ShouldBeTrue();
        replayed.Rebased.Values.Before.Value.ShouldBe([1, 2]);
        replayed.Rebased.Values.After.Value.ShouldBe([3, 4]);

        var applied = changes.RebaseOnto(ValuesOf(3, 4));
        applied.HasConflicts.ShouldBeFalse();
        applied.Rebased.IsEmpty.ShouldBeTrue();

        var conflicted = changes.RebaseOnto(ValuesOf(9, 9));
        conflicted.HasConflicts.ShouldBeTrue();
        conflicted.Conflicts[0].Kind.ShouldBe(SparseConflictKind.CustomStrategy);
    }
}
