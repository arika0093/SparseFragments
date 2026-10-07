using SparseFragments;

namespace SparseFragments.Tests;

public sealed class ChangeSetTests
{
    private static Optional<Settings.Fragment?> Present(Settings.Fragment fragment) =>
        Optional<Settings.Fragment?>.Present(fragment);

    private static Settings.Fragment MakeSettings(string? label = "initial", int retry = 3)
    {
        return new Settings.Fragment
        {
            Label = Optional<string?>.Present(label),
            RetryCount = Optional<int>.Present(retry),
        };
    }

    private static void AssertSame(
        Optional<Settings.Fragment?> left,
        Optional<Settings.Fragment?> right,
        string message
    )
    {
        Settings.Patch.Between(left, right).IsEmpty.ShouldBeTrue(message);
    }

    [Test]
    public void EmptyChangeSetBetweenIdenticalStates()
    {
        var before = Present(MakeSettings());
        var changes = Settings.ChangeSet.Between(before, before);
        changes.IsEmpty.ShouldBeTrue();
        changes.ToPatch().IsEmpty.ShouldBeTrue();
        changes.Changes.Count.ShouldBe(0);
    }

    [Test]
    public void ScalarSetUnsetAndNullTransitions()
    {
        var before = Present(MakeSettings("Alice", 1));
        var after = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        Settings.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();

        var toNull = Settings.ChangeSet.Between(
            before,
            Present(MakeSettings(null, 1))
        );
        toNull.IsEmpty.ShouldBeFalse();

        var unset = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Missing,
                RetryCount = Optional<int>.Present(1),
            }
        );
        var unsetChanges = Settings.ChangeSet.Between(before, unset);
        unsetChanges.IsEmpty.ShouldBeFalse();
        Settings.Patch.Between(unsetChanges.ToPatch().Apply(before), unset).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void RootMissingNullAndValueTransitions()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var nullState = Optional<Settings.Fragment?>.Present(null);
        var value = Present(MakeSettings());

        Settings.ChangeSet.Between(missing, missing).IsEmpty.ShouldBeTrue();
        Settings.ChangeSet.Between(missing, nullState).IsEmpty.ShouldBeFalse();
        Settings.ChangeSet.Between(nullState, value).IsEmpty.ShouldBeFalse();
        Settings.ChangeSet.Between(value, missing).IsEmpty.ShouldBeFalse();

        var changes = Settings.ChangeSet.Between(missing, value);
        Settings.Patch.Between(changes.ToPatch().Apply(missing), value).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NestedStructuralChangeIsPreserved()
    {
        var before = Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("a") }
                ),
            }
        );
        var after = Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("b") }
                ),
            }
        );
        var changes = Settings.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        Settings.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ScalarCollectionReplacement()
    {
        var before = Optional<ScalarSequenceHolder.Fragment?>.Present(
            new ScalarSequenceHolder.Fragment
            {
                Tags = Optional<List<string>>.Present(["a", "b"]),
            }
        );
        var after = Optional<ScalarSequenceHolder.Fragment?>.Present(
            new ScalarSequenceHolder.Fragment
            {
                Tags = Optional<List<string>>.Present(["c"]),
            }
        );
        var changes = ScalarSequenceHolder.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        ScalarSequenceHolder.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedAddRemoveEditReorder()
    {
        Optional<KeyedServerHolder.Fragment?> FragmentOf(params KeyedServer[] items)
        {
            return Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        }

        var b0 = FragmentOf(
            new KeyedServer { Id = "b", Name = "b", Count = 1 },
            new KeyedServer { Id = "c", Name = "c", Count = 2 }
        );
        var b1 = FragmentOf(
            new KeyedServer { Id = "b", Name = "b2", Count = 1 },
            new KeyedServer { Id = "d", Name = "d", Count = 3 }
        );
        var changes = KeyedServerHolder.ChangeSet.Between(b0, b1);
        changes.IsEmpty.ShouldBeFalse();
        KeyedServerHolder.Patch.Between(changes.ToPatch().Apply(b0), b1).IsEmpty.ShouldBeTrue();

        var reorder = FragmentOf(
            new KeyedServer { Id = "c", Name = "c", Count = 2 },
            new KeyedServer { Id = "b", Name = "b", Count = 1 }
        );
        var reorderChanges = KeyedServerHolder.ChangeSet.Between(b0, reorder);
        reorderChanges.IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void KeyChangeIsRemovePlusAdd()
    {
        Optional<KeyedServerHolder.Fragment?> FragmentOf(params KeyedServer[] items)
        {
            return Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        }

        var before = FragmentOf(new KeyedServer { Id = "a", Name = "a", Count = 1 });
        var after = FragmentOf(new KeyedServer { Id = "b", Name = "a", Count = 1 });
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        KeyedServerHolder.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void CustomMergeStrategyPreserved()
    {
        var before = Optional<StrategySettings.Fragment?>.Present(
            StrategySettings.Fragment.From(new StrategySettings { Values = [1, 2] })
        );
        var after = Optional<StrategySettings.Fragment?>.Present(
            StrategySettings.Fragment.From(new StrategySettings { Values = [3, 4] })
        );
        var changes = StrategySettings.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        StrategySettings.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void FromPatchEquivalence()
    {
        var baseline = Present(MakeSettings("Alice", 1));
        var patch = new Settings.Patch();
        patch.Label = "Bob";
        var changes = Settings.ChangeSet.FromPatch(baseline, patch);
        var expected = Settings.ChangeSet.Between(baseline, patch.Apply(baseline));
        Settings.Patch.Between(changes.ToPatch().Apply(baseline), expected.ToPatch().Apply(baseline))
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ToPatchApplicationEquivalence()
    {
        var before = Present(MakeSettings("Alice", 1));
        var after = Present(MakeSettings("Bob", 2));
        var changes = Settings.ChangeSet.Between(before, after);
        AssertSame(after, changes.ToPatch().Apply(before), "ToPatch applies to after");
    }

    [Test]
    public void InvertInvertRoundTrip()
    {
        var before = Present(MakeSettings("Alice", 1));
        var after = Present(MakeSettings("Bob", 2));
        var changes = Settings.ChangeSet.Between(before, after);
        var roundTripped = changes.Invert().Invert();
        Settings.Patch.Between(
            roundTripped.ToPatch().Apply(before),
            after
        ).IsEmpty.ShouldBeTrue();
        Settings.Patch.Between(
            changes.Invert().ToPatch().Apply(after),
            before
        ).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void SequentialCompose()
    {
        var b0 = Present(MakeSettings("a", 1));
        var b1 = Present(MakeSettings("b", 1));
        var b2 = Present(MakeSettings("b", 2));
        var first = Settings.ChangeSet.Between(b0, b1);
        var second = Settings.ChangeSet.Between(b1, b2);
        var composed = first.Compose(second);
        Settings.Patch.Between(composed.ToPatch().Apply(b0), b2).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void InvalidNonContiguousComposeThrows()
    {
        var b0 = Present(MakeSettings("a", 1));
        var b1 = Present(MakeSettings("b", 1));
        var b2 = Present(MakeSettings("c", 5));
        var first = Settings.ChangeSet.Between(b0, b1);
        var second = Settings.ChangeSet.Between(b1, b2);
        var broken = Settings.ChangeSet.Between(
            Present(MakeSettings("other", 9)),
            b2
        );
        Should.Throw<InvalidOperationException>(() => first.Compose(broken));
        _ = second;
    }

    [Test]
    public void CleanDisjointRebaseReplays()
    {
        var baseState = Present(MakeSettings("Alice", 20));
        var edited = Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("Alice"),
                RetryCount = Optional<int>.Present(21),
            }
        );
        var current = Present(MakeSettings("Bob", 20));
        var changes = Settings.ChangeSet.Between(baseState, edited);
        var result = changes.RebaseOnto(current);
        result.HasConflicts.ShouldBeFalse();
        var expected = Present(MakeSettings("Bob", 21));
        Settings.Patch.Between(result.Patch.ToPatch().Apply(current), expected).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AlreadyAppliedRebaseIsNoOp()
    {
        var baseState = Present(MakeSettings("Alice", 1));
        var edited = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(baseState, edited);
        var result = changes.RebaseOnto(edited);
        result.Patch.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ScalarConflictReported()
    {
        var baseState = Present(MakeSettings("Alice", 1));
        var edited = Present(MakeSettings("Bob", 1));
        var current = Present(MakeSettings("Carol", 1));
        var changes = Settings.ChangeSet.Between(baseState, edited);
        var result = changes.RebaseOnto(current);
        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void RebaseResultRelativeToCurrent()
    {
        var baseState = Present(MakeSettings("Alice", 1));
        var edited = Present(MakeSettings("Alice", 2));
        var current = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(baseState, edited);
        var result = changes.RebaseOnto(current);
        result.HasConflicts.ShouldBeFalse();
        var expected = Present(MakeSettings("Bob", 2));
        Settings.Patch.Between(result.Patch.ToPatch().Apply(current), expected).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NoOldBaselineRequiredAtRebaseTime()
    {
        var baseState = Present(MakeSettings("Alice", 20));
        var edited = Present(MakeSettings("Alice", 21));
        var changes = Settings.ChangeSet.Between(baseState, edited);
        var current = Present(MakeSettings("Bob", 20));
        var result = changes.RebaseOnto(current);
        result.HasConflicts.ShouldBeFalse();
    }

    [Test]
    public void NestedConflictReported()
    {
        var baseState = Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("a") }
                ),
            }
        );
        var edited = Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("b") }
                ),
            }
        );
        var current = Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("c") }
                ),
            }
        );
        var changes = Settings.ChangeSet.Between(baseState, edited);
        var result = changes.RebaseOnto(current);
        result.HasConflicts.ShouldBeTrue();
    }

    [Test]
    public void KeyedConflictReported()
    {
        Optional<KeyedServerHolder.Fragment?> FragmentOf(params KeyedServer[] items)
        {
            return Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        }

        var baseState = FragmentOf(new KeyedServer { Id = "a", Name = "x", Count = 1 });
        var edited = FragmentOf(new KeyedServer { Id = "a", Name = "y", Count = 1 });
        var current = FragmentOf(new KeyedServer { Id = "a", Name = "z", Count = 1 });
        var changes = KeyedServerHolder.ChangeSet.Between(baseState, edited);
        var result = changes.RebaseOnto(current);
        result.HasConflicts.ShouldBeTrue();
    }

    [Test]
    public void PatchRemainsMutableBaselineFree()
    {
        var patch = new Settings.Patch();
        patch.Label = "x";
        var next = new Settings.Patch();
        next.RetryCount = 5;
        var composed = patch.Compose(next);
        composed.Label.Value.ShouldBe("x");
        composed.RetryCount.Value.ShouldBe(5);
    }

    [Test]
    public void InspectionUsesSharedPropertyMetadata()
    {
        var before = Present(MakeSettings("Alice", 1));
        var after = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(before, after);
        changes.Changes.Count.ShouldBeGreaterThan(0);
        var patchChanges = changes.ToPatch().Changes;
        patchChanges.Count.ShouldBe(changes.Changes.Count);
    }
}
