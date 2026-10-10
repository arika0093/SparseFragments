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
    }

    [Test]
    public void ScalarSetRemoveAndNullTransitions()
    {
        var before = Present(MakeSettings("Alice", 1));
        var after = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        Settings.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();

        var toNull = Settings.ChangeSet.Between(before, Present(MakeSettings(null, 1)));
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
    public void EnumerateChangesFlattensScalarAndNestedTransitions()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("before"),
                RetryCount = Optional<int>.Missing,
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("host-before") }
                ),
            }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present(null),
                RetryCount = Optional<int>.Present(3),
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("host-after") }
                ),
            }
        );

        var entries = Settings
            .ChangeSet.Between(before, after)
            .EnumerateChanges()
            .ToDictionary(static change => change.Path);

        entries[Settings.SparsePath.Label].Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Changed);
        entries[Settings.SparsePath.Label].Before.Value.ShouldBe("before");
        entries[Settings.SparsePath.Label].After.IsPresent.ShouldBeTrue();
        entries[Settings.SparsePath.Label].After.Value.ShouldBeNull();
        entries[Settings.SparsePath.RetryCount].Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Added);
        entries[Settings.SparsePath.RetryCount].Before.IsPresent.ShouldBeFalse();
        entries[Settings.SparsePath.RetryCount].After.Value.ShouldBe(3);
        entries[Settings.SparsePath.Nested.Host]
            .Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Changed);
        entries[Settings.SparsePath.Nested.Host].Before.Value.ShouldBe("host-before");
        entries[Settings.SparsePath.Nested.Host].After.Value.ShouldBe("host-after");
    }

    [Test]
    public void EnumerateChangesRepresentsKeyedItemsAndOrder()
    {
        Optional<KeyedServerHolder.Fragment?> State(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );

        var before = State(
            new KeyedServer
            {
                Id = "b",
                Name = "before",
                Count = 1,
            },
            new KeyedServer
            {
                Id = "c",
                Name = "removed",
                Count = 2,
            }
        );
        var after = State(
            new KeyedServer
            {
                Id = "a",
                Name = "added",
                Count = 3,
            },
            new KeyedServer
            {
                Id = "b",
                Name = "after",
                Count = 1,
            }
        );
        var entries = KeyedServerHolder
            .ChangeSet.Between(before, after)
            .EnumerateChanges()
            .ToDictionary(static change => change.Path);

        entries[KeyedServerHolder.SparsePath.Items.Key("a")]
            .Kind.ShouldBe(KeyedServerHolder.ChangeSet.ChangeKind.Added);
        entries[KeyedServerHolder.SparsePath.Items.Key("c")]
            .Kind.ShouldBe(KeyedServerHolder.ChangeSet.ChangeKind.Removed);
        entries[KeyedServerHolder.SparsePath.Items.Key("b").Name].Before.Value.ShouldBe("before");
        entries[KeyedServerHolder.SparsePath.Items.Key("b").Name].After.Value.ShouldBe("after");
        entries[KeyedServerHolder.SparsePath.Items]
            .Kind.ShouldBe(KeyedServerHolder.ChangeSet.ChangeKind.Order);
        (
            (IReadOnlyList<string>)entries[KeyedServerHolder.SparsePath.Items].Before.Value!
        ).ShouldBe(["b", "c"]);
        ((IReadOnlyList<string>)entries[KeyedServerHolder.SparsePath.Items].After.Value!).ShouldBe([
            "a",
            "b",
        ]);
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
    public void EnumerateChangesReportsRootPresenceTransitions()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var nullState = Optional<Settings.Fragment?>.Present(null);
        var value = Present(MakeSettings());

        // Missing -> present-null is nonempty and enumerated as $root Added.
        var added = Settings.ChangeSet.Between(missing, nullState);
        added.IsEmpty.ShouldBeFalse();
        var addedEntries = added.EnumerateChanges().ToList();
        addedEntries.ShouldHaveSingleItem();
        addedEntries[0].PathText.ShouldBe("$root");
        addedEntries[0].Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Added);
        added.EnumerateChangedPaths().Select(static path => path.ToString()).ShouldBe(["$root"]);

        // Present-null -> missing reads as Removed.
        var removed = Settings.ChangeSet.Between(nullState, missing);
        removed.IsEmpty.ShouldBeFalse();
        removed.EnumerateChanges().Single().Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Removed);

        // Present-null -> present-value reads as Changed.
        var valued = Settings.ChangeSet.Between(nullState, value);
        valued.IsEmpty.ShouldBeFalse();
        valued.EnumerateChanges().Single().Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Changed);

        // Nested whole-child presence transitions are not dropped: the child
        // root entry appears under the parent member path.
        var nestedMissing = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Nested = Optional<Nested.Fragment?>.Missing }
        );
        var nestedPresent = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Nested = Optional<Nested.Fragment?>.Present(null) }
        );
        var nested = Settings.ChangeSet.Between(nestedMissing, nestedPresent);
        nested.IsEmpty.ShouldBeFalse();
        nested.EnumerateChanges().Select(static change => change.PathText).ShouldContain("Nested");
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
            new ScalarSequenceHolder.Fragment { Tags = Optional<List<string>>.Present(["a", "b"]) }
        );
        var after = Optional<ScalarSequenceHolder.Fragment?>.Present(
            new ScalarSequenceHolder.Fragment { Tags = Optional<List<string>>.Present(["c"]) }
        );
        var changes = ScalarSequenceHolder.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        ScalarSequenceHolder
            .Patch.Between(changes.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
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
            new KeyedServer
            {
                Id = "b",
                Name = "b",
                Count = 1,
            },
            new KeyedServer
            {
                Id = "c",
                Name = "c",
                Count = 2,
            }
        );
        var b1 = FragmentOf(
            new KeyedServer
            {
                Id = "b",
                Name = "b2",
                Count = 1,
            },
            new KeyedServer
            {
                Id = "d",
                Name = "d",
                Count = 3,
            }
        );
        var changes = KeyedServerHolder.ChangeSet.Between(b0, b1);
        changes.IsEmpty.ShouldBeFalse();
        KeyedServerHolder.Patch.Between(changes.ToPatch().Apply(b0), b1).IsEmpty.ShouldBeTrue();

        var reorder = FragmentOf(
            new KeyedServer
            {
                Id = "c",
                Name = "c",
                Count = 2,
            },
            new KeyedServer
            {
                Id = "b",
                Name = "b",
                Count = 1,
            }
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

        var before = FragmentOf(
            new KeyedServer
            {
                Id = "a",
                Name = "a",
                Count = 1,
            }
        );
        var after = FragmentOf(
            new KeyedServer
            {
                Id = "b",
                Name = "a",
                Count = 1,
            }
        );
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        KeyedServerHolder
            .Patch.Between(changes.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();
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
        StrategySettings
            .Patch.Between(changes.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();

        var beforeModel = new StrategySettings { Values = [1, 2] };
        var afterModel = new StrategySettings { Values = [3, 4] };
        var modelChanges = StrategySettings.ChangeSet.Between(beforeModel, afterModel);
        if (!modelChanges.TryApplyTo(beforeModel, out var applied))
        {
            throw new InvalidOperationException("Expected custom strategy model application.");
        }
        applied.Values.ShouldBe([3, 4]);
    }

    [Test]
    public void ModelBetweenMatchesPresenceAwareBetween()
    {
        var before = new Settings
        {
            Label = "before",
            RetryCount = 1,
            Nested = new Nested { Host = "before-host" },
            Plugins = ["base"],
        };
        var after = new Settings
        {
            Label = null,
            RetryCount = 2,
            Nested = null,
            Plugins = ["base", "extra"],
        };
        var beforeState = Present(Settings.Fragment.From(before));
        var afterState = Present(Settings.Fragment.From(after));

        var changes = Settings.ChangeSet.Between(before, after);
        var expected = Settings.ChangeSet.Between(beforeState, afterState);

        changes.IsEmpty.ShouldBe(expected.IsEmpty);
        Settings
            .Patch.Between(
                changes.ToPatch().Apply(beforeState),
                expected.ToPatch().Apply(beforeState)
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ModelBetweenAndTryApplyToSupportKeyedCollections()
    {
        var before = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer
                {
                    Id = "a",
                    Name = "old",
                    Count = 1,
                },
                new KeyedServer
                {
                    Id = "b",
                    Name = "keep",
                    Count = 2,
                },
            ],
        };
        var after = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer
                {
                    Id = "a",
                    Name = "new",
                    Count = 1,
                },
                new KeyedServer
                {
                    Id = "c",
                    Name = "added",
                    Count = 3,
                },
            ],
        };
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);

        if (!changes.TryApplyTo(before, out var updated, out var conflicts))
        {
            throw new InvalidOperationException(
                "Expected keyed model application to be clean: "
                    + string.Join(", ", conflicts.Select(static conflict => conflict.PathText))
            );
        }

        updated.Items.Select(static item => item.Id).ShouldBe(["a", "c"]);
        updated.Items.Single(static item => item.Id == "a").Name.ShouldBe("new");
        updated.Items.Single(static item => item.Id == "c").Count.ShouldBe(3);
        before.Items.Select(static item => item.Id).ShouldBe(["a", "b"]);
        before.Items[0].Name.ShouldBe("old");
    }

    [Test]
    public void ModelTryApplyToPreservesKeyedReorder()
    {
        var before = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "a", Name = "a" },
                new KeyedServer { Id = "b", Name = "b" },
            ],
        };
        var after = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "b", Name = "b" },
                new KeyedServer { Id = "a", Name = "a" },
            ],
        };
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);

        if (!changes.TryApplyTo(before, out var updated))
        {
            throw new InvalidOperationException("Expected the keyed reorder to be clean.");
        }

        updated.Items.Select(static item => item.Id).ShouldBe(["b", "a"]);
    }

    [Test]
    public void ModelFromPatchMatchesPresenceAwareFromPatch()
    {
        var baseline = MakeSettings("Alice", 1).ToModel();
        var patch = new Settings.Patch { Label = (string?)null };
        patch.Nested.Host = "db.local";

        var changes = Settings.ChangeSet.FromPatch(baseline, patch);
        var expected = Settings.ChangeSet.FromPatch(
            Present(Settings.Fragment.From(baseline)),
            patch
        );

        Settings
            .Patch.Between(
                changes.ToPatch().Apply(Present(Settings.Fragment.From(baseline))),
                expected.ToPatch().Apply(Present(Settings.Fragment.From(baseline)))
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void PatchApplyToModelMatchesFragmentPathWithoutMutatingSource()
    {
        var current = new Settings
        {
            Label = "before",
            RetryCount = 1,
            Nested = new Nested { Host = "before-host", Port = 5000 },
            Plugins = ["base"],
        };
        var patch = new Settings.Patch { Label = "after" };
        patch.Nested.Host = "after-host";

        var expected = Settings.Fragment.From(current).Apply(patch).ToModel();
        var actual = patch.ApplyTo(current);

        Settings
            .Patch.Between(
                Present(Settings.Fragment.From(actual)),
                Present(Settings.Fragment.From(expected))
            )
            .IsEmpty.ShouldBeTrue();
        current.Label.ShouldBe("before");
        current.Nested!.Host.ShouldBe("before-host");
        current.Plugins.ShouldBe(["base"]);
    }

    [Test]
    public void FromPatchEquivalence()
    {
        var baseline = Present(MakeSettings("Alice", 1));
        var patch = new Settings.Patch();
        patch.Label = "Bob";
        var changes = Settings.ChangeSet.FromPatch(baseline, patch);
        var expected = Settings.ChangeSet.Between(baseline, patch.Apply(baseline));
        Settings
            .Patch.Between(changes.ToPatch().Apply(baseline), expected.ToPatch().Apply(baseline))
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
        Settings.Patch.Between(roundTripped.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
        Settings
            .Patch.Between(changes.Invert().ToPatch().Apply(after), before)
            .IsEmpty.ShouldBeTrue();
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
        var broken = Settings.ChangeSet.Between(Present(MakeSettings("other", 9)), b2);
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
        Settings
            .Patch.Between(result.Rebased.ToPatch().Apply(current), expected)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ModelRebaseMatchesPresenceAwareRebase()
    {
        var baseline = MakeSettings("Alice", 20).ToModel();
        var edited = MakeSettings("Alice", 21).ToModel();
        var current = MakeSettings("Bob", 20).ToModel();
        var changes = Settings.ChangeSet.Between(baseline, edited);

        var actual = changes.RebaseOnto(current);
        var expected = changes.RebaseOnto(Present(Settings.Fragment.From(current)));

        actual.HasConflicts.ShouldBe(expected.HasConflicts);
        Settings
            .Patch.Between(
                actual.Rebased.ToPatch().Apply(Present(Settings.Fragment.From(current))),
                expected.Rebased.ToPatch().Apply(Present(Settings.Fragment.From(current)))
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ModelRebaseRecognizesAlreadyAppliedChange()
    {
        var changes = Settings.ChangeSet.Between(
            MakeSettings("Alice", 1).ToModel(),
            MakeSettings("Bob", 1).ToModel()
        );
        var current = MakeSettings("Bob", 1).ToModel();
        var rebased = changes.RebaseOnto(current);

        rebased.HasConflicts.ShouldBeFalse();
        rebased.Rebased.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void TryApplyToReturnsNonNullModelWhenConflictFree()
    {
        var changes = Settings.ChangeSet.Between(
            MakeSettings("Alice", 20).ToModel(),
            MakeSettings("Alice", 21).ToModel()
        );
        var current = MakeSettings("Bob", 20).ToModel();

        if (!changes.TryApplyTo(current, out var updated))
        {
            throw new InvalidOperationException("Expected conflict-free application.");
        }

        updated.RetryCount.ShouldBe(21);
        updated.Label.ShouldBe("Bob");
    }

    [Test]
    public void TryApplyToReturnsConflictsWithoutPartiallyAppliedModel()
    {
        var changes = Settings.ChangeSet.Between(
            MakeSettings("Alice", 1).ToModel(),
            MakeSettings("Bob", 2).ToModel()
        );
        var current = MakeSettings("Carol", 1).ToModel();

        if (changes.TryApplyTo(current, out var updated, out var conflicts))
        {
            throw new InvalidOperationException("Expected a conflict.");
        }

        updated.ShouldBeNull();
        conflicts.Select(static conflict => conflict.PathText).ShouldContain("Label");
        conflicts.ShouldNotContain(static conflict => conflict.PathText == "RetryCount");
        current.RetryCount.ShouldBe(1);
    }

    [Test]
    public void AlreadyAppliedRebaseIsNoOp()
    {
        var baseState = Present(MakeSettings("Alice", 1));
        var edited = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(baseState, edited);
        var result = changes.RebaseOnto(edited);
        result.Rebased.IsEmpty.ShouldBeTrue();
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
        Settings
            .Patch.Between(result.Rebased.ToPatch().Apply(current), expected)
            .IsEmpty.ShouldBeTrue();
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

        var baseState = FragmentOf(
            new KeyedServer
            {
                Id = "a",
                Name = "x",
                Count = 1,
            }
        );
        var edited = FragmentOf(
            new KeyedServer
            {
                Id = "a",
                Name = "y",
                Count = 1,
            }
        );
        var current = FragmentOf(
            new KeyedServer
            {
                Id = "a",
                Name = "z",
                Count = 1,
            }
        );
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
    public void TypedTransitionsObserveMemberChanges()
    {
        var before = Present(MakeSettings("Alice", 1));
        var after = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        changes.Label.IsChanged.ShouldBeTrue();
        changes.Label.Before.Value.ShouldBe("Alice");
        changes.Label.After.Value.ShouldBe("Bob");
        changes.RetryCount.IsChanged.ShouldBeFalse();
        var patch = changes.ToPatch();
        patch.IsEmpty.ShouldBeFalse();
        Settings.Patch.Between(patch.Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void TypedScalarPresenceTransitions()
    {
        var before = Present(MakeSettings("Alice", 1));
        var same = Present(MakeSettings("Alice", 1));
        var unchanged = Settings.ChangeSet.Between(before, same);
        unchanged.Label.IsChanged.ShouldBeFalse();
        // Issue #96: unchanged members retain nothing; Before/After are Missing.
        unchanged.Label.Before.IsPresent.ShouldBeFalse();
        unchanged.Label.After.IsPresent.ShouldBeFalse();
        unchanged.RetryCount.IsChanged.ShouldBeFalse();
        unchanged.RetryCount.Before.IsPresent.ShouldBeFalse();

        var after = Present(MakeSettings("Bob", 1));
        var changes = Settings.ChangeSet.Between(before, after);
        changes.Label.IsChanged.ShouldBeTrue();
        changes.Label.Before.Value.ShouldBe("Alice");
        changes.Label.After.Value.ShouldBe("Bob");
        changes.RetryCount.IsChanged.ShouldBeFalse();
        changes.RetryCount.Before.IsPresent.ShouldBeFalse();
        changes.RetryCount.After.IsPresent.ShouldBeFalse();

        var toNull = Settings.ChangeSet.Between(before, Present(MakeSettings(null, 1)));
        toNull.Label.IsChanged.ShouldBeTrue();
        toNull.Label.Before.Value.ShouldBe("Alice");
        toNull.Label.After.IsPresent.ShouldBeTrue();
        toNull.Label.After.Value.ShouldBeNull();

        var unsetAfter = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Missing,
                RetryCount = Optional<int>.Present(1),
            }
        );
        var unset = Settings.ChangeSet.Between(before, unsetAfter);
        unset.Label.IsChanged.ShouldBeTrue();
        unset.Label.Before.Value.ShouldBe("Alice");
        unset.Label.After.IsPresent.ShouldBeFalse();

        var missingBefore = Optional<Settings.Fragment?>.Present(new Settings.Fragment());
        var setAfter = Present(MakeSettings("x", 3));
        var missingToValue = Settings.ChangeSet.Between(missingBefore, setAfter);
        missingToValue.Label.IsChanged.ShouldBeTrue();
        missingToValue.Label.Before.IsPresent.ShouldBeFalse();
        missingToValue.Label.After.Value.ShouldBe("x");
    }

    [Test]
    public void TypedScalarCollectionBeforeAfter()
    {
        Optional<ScalarSequenceHolder.Fragment?> State(ScalarSequenceHolder m) =>
            Optional<ScalarSequenceHolder.Fragment?>.Present(ScalarSequenceHolder.Fragment.From(m));
        var before = State(new ScalarSequenceHolder { Tags = ["a", "b"], Numbers = [1, 2] });
        var after = State(new ScalarSequenceHolder { Tags = ["c"], Numbers = [1, 2] });
        var changes = ScalarSequenceHolder.ChangeSet.Between(before, after);
        changes.Tags.IsChanged.ShouldBeTrue();
        changes.Tags.Before.Value.ShouldBe(["a", "b"]);
        changes.Tags.After.Value.ShouldBe(["c"]);
        changes.Numbers.IsChanged.ShouldBeFalse();
        // Issue #96: unchanged collection members retain nothing.
        changes.Numbers.Before.IsPresent.ShouldBeFalse();
        changes.Numbers.After.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void TypedNestedTransitions()
    {
        Optional<Settings.Fragment?> State(string host) =>
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Nested = Optional<Nested.Fragment?>.Present(
                        new Nested.Fragment { Host = Optional<string>.Present(host) }
                    ),
                }
            );
        var unchanged = Settings.ChangeSet.Between(State("a"), State("a"));
        unchanged.Nested.IsEmpty.ShouldBeTrue();
        unchanged.Nested.Host.IsChanged.ShouldBeFalse();

        var changes = Settings.ChangeSet.Between(State("a"), State("b"));
        changes.Nested.IsEmpty.ShouldBeFalse();
        changes.Nested.Host.IsChanged.ShouldBeTrue();
        changes.Nested.Host.Before.Value.ShouldBe("a");
        changes.Nested.Host.After.Value.ShouldBe("b");
        changes.Nested.Port.IsChanged.ShouldBeFalse();

        var missingNested = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Present(new Settings.Fragment()),
            State("h")
        );
        missingNested.Nested.IsEmpty.ShouldBeFalse();
        missingNested.Nested.Host.IsChanged.ShouldBeTrue();
        missingNested.Nested.Host.Before.IsPresent.ShouldBeFalse();
        missingNested.Nested.Host.After.Value.ShouldBe("h");
    }

    [Test]
    public void TypedKeyedAddRemoveEdit()
    {
        Optional<KeyedServerHolder.Fragment?> F(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        var b0 = F(
            new KeyedServer
            {
                Id = "b",
                Name = "b",
                Count = 1,
            },
            new KeyedServer
            {
                Id = "c",
                Name = "c",
                Count = 2,
            }
        );
        var b1 = F(
            new KeyedServer
            {
                Id = "b",
                Name = "b2",
                Count = 1,
            },
            new KeyedServer
            {
                Id = "d",
                Name = "d",
                Count = 3,
            }
        );
        var changes = KeyedServerHolder.ChangeSet.Between(b0, b1);
        var quests = changes.Items;
        quests.IsChanged.ShouldBeTrue();
        quests.IsEmpty.ShouldBeFalse();
        quests.Added.Select(e => e.Id).ShouldBe(["d"]);
        quests.Removed.Select(e => e.Id).ShouldBe(["c"]);
        quests.Removed.Single().Name.ShouldBe("c");
        quests.Edited.Count.ShouldBe(1);
        quests.Edited.ContainsKey("b").ShouldBeTrue();
        quests.Edited["b"].Name.IsChanged.ShouldBeTrue();
        quests.Edited["b"].Name.Before.Value.ShouldBe("b");
        quests.Edited["b"].Name.After.Value.ShouldBe("b2");
        quests.BeforeOrder.ShouldBe(["b", "c"]);
        quests.AfterOrder.ShouldBe(["b", "d"]);
    }

    [Test]
    public void TypedKeyedNormalizationIgnoresPatchShape()
    {
        Optional<KeyedServerHolder.Fragment?> F(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        var b = new KeyedServer
        {
            Id = "b",
            Name = "b",
            Count = 1,
        };
        var c = new KeyedServer
        {
            Id = "c",
            Name = "c",
            Count = 2,
        };
        var before = F(
            new KeyedServer
            {
                Id = "a",
                Name = "a",
                Count = 0,
            },
            b,
            c
        );
        var after = F(
            b,
            new KeyedServer
            {
                Id = "c",
                Name = "c",
                Count = 2,
            }
        );

        var granular = new KeyedServerHolder.Patch();
        granular.Items.Remove("a");
        granular.Items.SetOrder(["b", "c"]);
        var fromGranular = KeyedServerHolder.ChangeSet.FromPatch(before, granular);

        var whole = new KeyedServerHolder.Patch();
        whole.Items.Set([
            b,
            new KeyedServer
            {
                Id = "c",
                Name = "c",
                Count = 2,
            },
        ]);
        var fromWhole = KeyedServerHolder.ChangeSet.FromPatch(before, whole);

        TextCheck(fromGranular.Items, fromWhole.Items);
        static void TextCheck(
            KeyedServerHolder.ChangeSet.ItemsTransition left,
            KeyedServerHolder.ChangeSet.ItemsTransition right
        )
        {
            left.Added.Select(e => e.Id).ShouldBe(right.Added.Select(e => e.Id).ToArray());
            left.Removed.Select(e => e.Id).ShouldBe(right.Removed.Select(e => e.Id).ToArray());
            left.Edited.Count.ShouldBe(right.Edited.Count);
            left.BeforeOrder.ShouldBe(right.BeforeOrder.ToArray());
            left.AfterOrder.ShouldBe(right.AfterOrder.ToArray());
            left.OrderChanged.ShouldBe(right.OrderChanged);
        }

        var canonical = KeyedServerHolder.ChangeSet.Between(before, after);
        canonical.Items.Added.Count.ShouldBe(0);
        canonical.Items.Removed.Select(e => e.Id).ShouldBe(["a"]);
        canonical.Items.Removed.Single().Name.ShouldBe("a");
        canonical.Items.Edited.Count.ShouldBe(0);
        canonical.Items.BeforeOrder.ShouldBe(["a", "b", "c"]);
        canonical.Items.AfterOrder.ShouldBe(["b", "c"]);
        fromGranular.Items.Removed.Select(e => e.Id).ShouldBe(["a"]);
        fromWhole.Items.Removed.Select(e => e.Id).ShouldBe(["a"]);

        _ = after;
    }

    [Test]
    public void TypedKeyChangeIsRemovePlusAdd()
    {
        Optional<KeyedServerHolder.Fragment?> F(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        var changes = KeyedServerHolder.ChangeSet.Between(
            F(
                new KeyedServer
                {
                    Id = "a",
                    Name = "a",
                    Count = 1,
                }
            ),
            F(
                new KeyedServer
                {
                    Id = "b",
                    Name = "a",
                    Count = 1,
                }
            )
        );
        changes.Items.Added.Select(e => e.Id).ShouldBe(["b"]);
        changes.Items.Removed.Select(e => e.Id).ShouldBe(["a"]);
        changes.Items.Edited.Count.ShouldBe(0);
    }

    [Test]
    public void TypedKeyedEnumerationAndReorderSemantics()
    {
        Optional<KeyedServerHolder.Fragment?> F(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        KeyedServer S(string id) =>
            new()
            {
                Id = id,
                Name = id,
                Count = 1,
            };

        var membership = KeyedServerHolder.ChangeSet.Between(
            F(S("a"), S("b"), S("c")),
            F(S("b"), S("c"))
        );
        membership.Items.OrderChanged.ShouldBeTrue();
        membership.Items.Added.Count.ShouldBe(0);
        membership.Items.Removed.Select(e => e.Id).ShouldBe(["a"]);
        membership.Items.Select(i => i.Key).ShouldBe(["a"]);
        foreach (var item in membership.Items)
        {
            item.IsReordered.ShouldBeFalse(
                $"key {item.Key} must not be reordered by membership-only shift"
            );
        }
        membership.Items.BeforeOrder.ShouldBe(["a", "b", "c"]);
        membership.Items.AfterOrder.ShouldBe(["b", "c"]);

        var reorder = KeyedServerHolder.ChangeSet.Between(
            F(S("a"), S("b"), S("c")),
            F(S("b"), S("a"), S("c"))
        );
        reorder.Items.OrderChanged.ShouldBeTrue();
        reorder.Items.Added.Count.ShouldBe(0);
        reorder.Items.Removed.Count.ShouldBe(0);
        reorder.Items.Edited.Count.ShouldBe(0);
        var byKey = reorder.Items.ToDictionary(i => i.Key);
        byKey.ContainsKey("c").ShouldBeFalse();
        byKey["a"].IsReordered.ShouldBeTrue();
        byKey["b"].IsReordered.ShouldBeTrue();
        byKey["a"].BeforeIndex.ShouldBe(0);
        byKey["a"].AfterIndex.ShouldBe(1);
        byKey["b"].BeforeIndex.ShouldBe(1);
        byKey["b"].AfterIndex.ShouldBe(0);
        byKey["a"].Before.IsPresent.ShouldBeFalse();
        byKey["a"].After.IsPresent.ShouldBeFalse();
        byKey["a"].IsAdded.ShouldBeFalse();
        byKey["a"].IsRemoved.ShouldBeFalse();
        byKey["a"].IsEdited.ShouldBeFalse();

        var add = KeyedServerHolder.ChangeSet.Between(F(S("a"), S("b")), F(S("a"), S("b"), S("c")));
        var addedItem = add.Items.Single(i => i.IsAdded);
        addedItem.Key.ShouldBe("c");
        addedItem.BeforeIndex.ShouldBe(-1);
        addedItem.AfterIndex.ShouldBe(2);
        addedItem.Before.IsPresent.ShouldBeFalse();
        addedItem.After.Value!.Id.ShouldBe("c");
        addedItem.Edit.IsEmpty.ShouldBeFalse();

        var edited = KeyedServerHolder.ChangeSet.Between(
            F(
                new KeyedServer
                {
                    Id = "a",
                    Name = "old",
                    Count = 1,
                }
            ),
            F(
                new KeyedServer
                {
                    Id = "a",
                    Name = "new",
                    Count = 1,
                }
            )
        );
        var editItem = edited.Items.Single();
        editItem.IsEdited.ShouldBeTrue();
        editItem.IsAdded.ShouldBeFalse();
        editItem.IsRemoved.ShouldBeFalse();
        editItem.Before.IsPresent.ShouldBeFalse();
        editItem.After.IsPresent.ShouldBeFalse();
        editItem.Edit.Name.After.Value.ShouldBe("new");
    }

    [Test]
    public void TypedDictionaryTransitions()
    {
        Optional<ScalarDictHolder.Fragment?> S(ScalarDictHolder m) =>
            Optional<ScalarDictHolder.Fragment?>.Present(ScalarDictHolder.Fragment.From(m));
        var changes = ScalarDictHolder.ChangeSet.Between(
            S(
                new ScalarDictHolder
                {
                    Scores = new() { ["a"] = 1, ["b"] = 2 },
                }
            ),
            S(
                new ScalarDictHolder
                {
                    Scores = new() { ["b"] = 3, ["c"] = 4 },
                }
            )
        );
        var scores = changes.Scores;
        scores.IsChanged.ShouldBeTrue();
        scores.Added.Count.ShouldBe(1);
        scores.Added["c"].ShouldBe(4);
        scores.Removed.Count.ShouldBe(1);
        scores.Removed["a"].ShouldBe(1);
        scores.Edited.Count.ShouldBe(1);
        scores.Edited["b"].ShouldBe(3);
        var byKey = scores.ToDictionary(i => i.Key);
        byKey["c"].IsAdded.ShouldBeTrue();
        byKey["c"].After.Value.ShouldBe(4);
        byKey["a"].IsRemoved.ShouldBeTrue();
        byKey["a"].Before.Value.ShouldBe(1);
        byKey["b"].IsEdited.ShouldBeTrue();
        byKey["b"].Before.Value.ShouldBe(2);
        byKey["b"].After.Value.ShouldBe(3);

        Optional<StructuralDictHolder.Fragment?> T(StructuralDictHolder m) =>
            Optional<StructuralDictHolder.Fragment?>.Present(StructuralDictHolder.Fragment.From(m));
        var structural = StructuralDictHolder.ChangeSet.Between(
            T(
                new StructuralDictHolder
                {
                    Servers = new()
                    {
                        ["web"] = new KeyedServer { Id = "s1", Name = "Old" },
                    },
                }
            ),
            T(
                new StructuralDictHolder
                {
                    Servers = new()
                    {
                        ["web"] = new KeyedServer { Id = "s1", Name = "New" },
                        ["db"] = new KeyedServer { Id = "s2", Name = "Db" },
                    },
                }
            )
        );
        structural.Servers.Added["db"].Id.ShouldBe("s2");
        structural.Servers.Removed.Count.ShouldBe(0);
        structural.Servers.Edited["web"].Name.After.Value.ShouldBe("New");
    }

    [Test]
    public void TypedProjectionsAreReadOnly()
    {
        Optional<KeyedServerHolder.Fragment?> F(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        var changes = KeyedServerHolder.ChangeSet.Between(
            F(new KeyedServer { Id = "a" }, new KeyedServer { Id = "b" }),
            F(new KeyedServer { Id = "b", Name = "B2" }, new KeyedServer { Id = "c" })
        );
        Should.Throw<System.NotSupportedException>(() =>
            ((System.Collections.Generic.IList<KeyedServer>)changes.Items.Added).Add(
                new KeyedServer { Id = "x" }
            )
        );
        Should.Throw<System.NotSupportedException>(() =>
            ((System.Collections.Generic.IList<KeyedServer>)changes.Items.Removed).Add(
                new KeyedServer { Id = "x" }
            )
        );
        Should.Throw<System.NotSupportedException>(() =>
            ((System.Collections.Generic.IList<string>)changes.Items.BeforeOrder).Add("x")
        );

        Optional<ScalarDictHolder.Fragment?> S(ScalarDictHolder m) =>
            Optional<ScalarDictHolder.Fragment?>.Present(ScalarDictHolder.Fragment.From(m));
        var dict = ScalarDictHolder.ChangeSet.Between(
            S(new ScalarDictHolder { Scores = new() { ["a"] = 1 } }),
            S(new ScalarDictHolder { Scores = new() { ["a"] = 2 } })
        );
        Should.Throw<System.NotSupportedException>(() =>
            ((System.Collections.Generic.IDictionary<string, int>)dict.Scores.Edited).Add("x", 1)
        );
    }

    [Test]
    public void RootChangeSetIsNotEnumerable()
    {
        typeof(Settings.ChangeSet)
            .GetInterfaces()
            .ShouldNotContain(t =>
                t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>)
            );
        typeof(Settings.ChangeSet)
            .GetInterfaces()
            .ShouldNotContain(t => t == typeof(System.Collections.IEnumerable));
    }

    [Test]
    public void TypedTupleKeyTransition()
    {
        CompositeServer S(string tenant, string id) =>
            new()
            {
                TenantId = tenant,
                Id = id,
                Name = tenant + id,
            };
        Optional<CompositeServerHolder.Fragment?> StateOf(CompositeServerHolder m) =>
            Optional<CompositeServerHolder.Fragment?>.Present(
                CompositeServerHolder.Fragment.From(m)
            );
        var changes = CompositeServerHolder.ChangeSet.Between(
            StateOf(new CompositeServerHolder { Items = [S("t1", "a")] }),
            StateOf(new CompositeServerHolder { Items = [S("t1", "a"), S("t2", "a")] })
        );
        changes.Items.Added.Count.ShouldBe(1);
        changes.Items.Removed.Count.ShouldBe(0);
        changes.Items.Added[0].TenantId.ShouldBe("t2");
        changes.Items.BeforeOrder.Count.ShouldBe(1);
        changes.Items.AfterOrder.Count.ShouldBe(2);
    }

    [Test]
    public void PayloadExcludesTypedConvenienceProjections()
    {
        var before = Present(MakeSettings("Alice", 1));
        var after = Present(MakeSettings("Bob", 2));
        var changes = Settings.ChangeSet.Between(before, after);
        var json = System.Text.Json.JsonSerializer.Serialize(changes.ToPayload());
        json.ShouldNotContain("Added");
        json.ShouldNotContain("Removed");
        json.ShouldNotContain("Edited");
        json.ShouldNotContain("IsChanged");
        json.ShouldNotContain("BeforeOrder");
        var back = System
            .Text.Json.JsonSerializer.Deserialize<Settings.ChangePayload>(json)!
            .ToChangeSet();
        back.Label.IsChanged.ShouldBeTrue();
        back.Label.After.Value.ShouldBe("Bob");
        back.RetryCount.After.Value.ShouldBe(2);
        Settings.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();

        Optional<KeyedServerHolder.Fragment?> F(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        var keyed = KeyedServerHolder.ChangeSet.Between(
            F(new KeyedServer { Id = "a", Name = "A" }),
            F(new KeyedServer { Id = "a", Name = "B" }, new KeyedServer { Id = "b" })
        );
        var keyedJson = System.Text.Json.JsonSerializer.Serialize(keyed.ToPayload());
        keyedJson.ShouldNotContain("Added");
        var keyedBack = System
            .Text.Json.JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(keyedJson)!
            .ToChangeSet();
        keyedBack.Items.Added.Select(e => e.Id).ShouldBe(["b"]);
        keyedBack.Items.Edited["a"].Name.After.Value.ShouldBe("B");
    }
}
