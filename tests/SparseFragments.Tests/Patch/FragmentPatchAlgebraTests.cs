using SparseFragments;

namespace SparseFragments.Tests;

public sealed class FragmentPatchAlgebraTests
{
    [Test]
    public void AlgebraLawsHoldAcrossSparseRootAndNestedOperations()
    {
        var states = new[]
        {
            Optional<Settings.Fragment?>.Missing,
            Optional<Settings.Fragment?>.Present(null),
            Optional<Settings.Fragment?>.Present(new Settings.Fragment()),
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("initial"),
                    RetryCount = Optional<int>.Present(7),
                    Nested = Optional<Nested.Fragment?>.Present(
                        new Nested.Fragment { Host = Optional<string>.Present("initial") }
                    ),
                }
            ),
        };
        var wholeNull = new Settings.Patch();
        wholeNull.SetNull();
        var wholeUnset = new Settings.Patch();
        wholeUnset.Unset();
        var wholeWithEdits = new Settings.Patch();
        wholeWithEdits.Set(new Settings { RetryCount = 5 });
        wholeWithEdits.RetryCount = 8;
        wholeWithEdits.Nested.Host = "replacement";
        var nestedEdit = new Settings.Patch();
        nestedEdit.Nested.Port = 42;
        var nestedUnset = new Settings.Patch();
        nestedUnset.Nested.Unset();
        var nestedNull = new Settings.Patch();
        nestedNull.Nested.SetNull();
        var nestedReplacement = new Settings.Patch();
        nestedReplacement.Nested.Set(new Nested { Host = "replacement", Port = 12 });
        var memberUnset = new Settings.Patch();
        memberUnset.Label = FragmentOperation<string?>.Unset;
        var patches = new[]
        {
            new Settings.Patch(),
            new Settings.Patch { RetryCount = 0, Label = (string?)null },
            memberUnset,
            wholeNull,
            wholeUnset,
            wholeWithEdits,
            nestedEdit,
            nestedUnset,
            nestedNull,
            nestedReplacement,
        };

        var stateIndex = 0;
        foreach (var state in states)
        {
            var firstIndex = 0;
            foreach (var first in patches)
            {
                AssertSame(
                    state,
                    Apply(first.Invert(state), Apply(first, state)),
                    $"invert round-trip (state {stateIndex}, patch {firstIndex})"
                );
                AssertSame(
                    Apply(first, state),
                    Apply(first.Compose(new Settings.Patch()), state),
                    $"compose right identity (state {stateIndex}, patch {firstIndex})"
                );
                AssertSame(
                    Apply(first, state),
                    Apply(new Settings.Patch().Compose(first), state),
                    $"compose left identity (state {stateIndex}, patch {firstIndex})"
                );
                var secondIndex = 0;
                foreach (var second in patches)
                {
                    AssertSame(
                        Apply(second, Apply(first, state)),
                        Apply(first.Compose(second), state),
                        $"compose matches sequential apply (state {stateIndex}, patches {firstIndex}/{secondIndex})"
                    );
                    var thirdIndex = 0;
                    foreach (var third in patches)
                    {
                        AssertSame(
                            Apply(first.Compose(second).Compose(third), state),
                            Apply(first.Compose(second.Compose(third)), state),
                            $"compose associativity (state {stateIndex}, patches {firstIndex}/{secondIndex}/{thirdIndex})"
                        );
                        thirdIndex++;
                    }

                    secondIndex++;
                }

                firstIndex++;
            }

            stateIndex++;
        }
    }

    [Test]
    public void BetweenPreservesMissingNullAndValueStates()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Enabled = Optional<bool>.Present(true),
                Label = Optional<string?>.Present("keep"),
            }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Enabled = Optional<bool>.Present(false),
                Label = Optional<string?>.Present(null),
                RetryCount = Optional<int>.Present(9),
            }
        );

        var patch = Settings.Patch.Between(before, after);

        AssertSame(after, Apply(patch, before), "applying Between(before, after) reaches after");
        AssertSame(
            before,
            Apply(patch.Invert(before), Apply(patch, before)),
            "inverting relative to the baseline restores before"
        );

        var inverted = patch.Invert(before);
        inverted.RetryCount.Kind.ShouldBe(FragmentOperationKind.Unset);
        inverted.Label.Value.ShouldBe("keep");
    }

    [Test]
    public void BetweenRootPresenceTransitionsRoundTrip()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var presentNull = Optional<Settings.Fragment?>.Present(null);
        var presentValue = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { RetryCount = Optional<int>.Present(4) }
        );

        var transitions = new (
            Optional<Settings.Fragment?> Before,
            Optional<Settings.Fragment?> After
        )[]
        {
            (missing, presentNull),
            (missing, presentValue),
            (presentNull, presentValue),
            (presentValue, presentNull),
            (presentValue, missing),
        };

        var transitionIndex = 0;
        foreach (var (before, after) in transitions)
        {
            var patch = Settings.Patch.Between(before, after);
            var applied = Apply(patch, before);
            AssertSame(after, applied, $"root transition {transitionIndex} applies");
            AssertSame(
                before,
                Apply(patch.Invert(before), applied),
                $"root transition {transitionIndex} inverts"
            );
            transitionIndex++;
        }
    }

    [Test]
    public void BetweenApplyAndInvertRoundTripAcrossDeterministicMatrix()
    {
        var states = new[]
        {
            Optional<Settings.Fragment?>.Missing,
            Optional<Settings.Fragment?>.Present(null),
            Optional<Settings.Fragment?>.Present(new Settings.Fragment()),
            Optional<Settings.Fragment?>.Present(
                Settings.Fragment.From(new Settings())
            ),
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("a"),
                    RetryCount = Optional<int>.Present(1),
                }
            ),
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present(null),
                    Nested = Optional<Nested.Fragment?>.Present(null),
                }
            ),
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Nested = Optional<Nested.Fragment?>.Present(
                        new Nested.Fragment
                        {
                            Host = Optional<string>.Present("h"),
                            Port = Optional<int>.Present(1),
                        }
                    ),
                    Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
                }
            ),
        };

        var pairIndex = 0;
        foreach (var before in states)
        {
            foreach (var after in states)
            {
                var patch = Settings.Patch.Between(before, after);
                AssertSame(after, Apply(patch, before), $"Between/apply pair {pairIndex}");
                AssertSame(
                    before,
                    Apply(patch.Invert(before), Apply(patch, before)),
                    $"Between/invert pair {pairIndex}"
                );
                pairIndex++;
            }
        }

        var stateIndex = 0;
        foreach (var state in states)
        {
            AssertSame(state, Apply(new Settings.Patch(), state), $"empty patch no-op {stateIndex}");
            AssertSame(
                state,
                Apply(Settings.Patch.Between(state, state), state),
                $"Between-identical-states no-op {stateIndex}"
            );
            stateIndex++;
        }
    }

    [Test]
    public void ComposeIdentityAndAssociativityMatchSequentialApply()
    {
        var state = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { Enabled = true, Label = "start" })
        );
        var first = new Settings.Patch { RetryCount = 12, Label = (string?)null };
        var second = new Settings.Patch { Enabled = false };
        var third = new Settings.Patch();
        third.Nested.Port = 7000;

        AssertSame(
            Apply(first, state),
            Apply(first.Compose(new Settings.Patch()), state),
            "compose right identity"
        );
        AssertSame(
            Apply(first, state),
            Apply(new Settings.Patch().Compose(first), state),
            "compose left identity"
        );

        var leftAssociated = first.Compose(second).Compose(third);
        var rightAssociated = first.Compose(second.Compose(third));
        AssertSame(
            Apply(leftAssociated, state),
            Apply(rightAssociated, state),
            "compose associativity"
        );

        var sequential = Apply(second, Apply(first, state));
        AssertSame(sequential, Apply(first.Compose(second), state), "compose matches sequential");
    }

    [Test]
    public void WholeContributionOperationsComposeWithMemberOperations()
    {
        var state = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { RetryCount = 1 })
        );
        var whole = new Settings.Patch();
        whole.Set(new Settings { RetryCount = 5, Enabled = false });
        var memberAfterWhole = new Settings.Patch { RetryCount = 7 };
        var memberBeforeWhole = new Settings.Patch { Enabled = true };

        AssertSame(
            Apply(memberAfterWhole, Apply(whole, state)),
            Apply(whole.Compose(memberAfterWhole), state),
            "member edits after a whole contribution"
        );
        AssertSame(
            Apply(whole, Apply(memberBeforeWhole, state)),
            Apply(memberBeforeWhole.Compose(whole), state),
            "whole contribution after member edits"
        );
    }

    [Test]
    public void NestedPatchCompositionAndInversionRoundTrip()
    {
        var before = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("host") }
                ),
            }
        );
        var after = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Port = Optional<int>.Present(6000) }
                ),
            }
        );

        var patch = Settings.Patch.Between(before, after);
        var rebound = Apply(patch, before);
        AssertSame(after, rebound, "nested Between applies");
        AssertSame(before, Apply(patch.Invert(before), rebound), "nested patch inverts");

        var noop = Settings.Patch.Between(rebound, rebound);
        noop.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void CollectionAlgebraLawsHoldViaOracle()
    {
        // Set union: order-independent, duplicate-free.
        var s0 = SetState("a");
        var s1 = SetState("a", "b");
        var s2 = SetState("b", "c");
        var setFirst = SetSettings.Patch.Between(s0, s1);
        var setSecond = SetSettings.Patch.Between(s1, s2);
        SemanticOracle.AssertEqual(s1, setFirst.Apply(s0), "set Between/apply");
        SemanticOracle.AssertEqual(
            s2,
            setFirst.Compose(setSecond).Apply(s0),
            "set compose matches sequential apply"
        );
        SemanticOracle.AssertEqual(
            s0,
            setFirst.Compose(setSecond).Invert(s0).Apply(setFirst.Compose(setSecond).Apply(s0)),
            "set invert round-trip"
        );
        SemanticOracle.AssertEqual(
            s1,
            SetSettings.Patch.Between(s1, s1).Apply(s1),
            "set already-applied patch is a no-op"
        );

        // Dictionaries: key/value semantics, insertion order irrelevant.
        var d0 = DictState(("first", 1));
        var d1 = DictState(("first", 2), ("second", 3));
        var dictPatch = DictionarySettings.Patch.Between(d0, d1);
        SemanticOracle.AssertEqual(d1, dictPatch.Apply(d0), "dictionary Between/apply");
        SemanticOracle.AssertEqual(
            d0,
            dictPatch.Invert(d0).Apply(dictPatch.Apply(d0)),
            "dictionary invert round-trip"
        );

        // Scalar sequences: whole-value, order-sensitive (duplicates preserved).
        var q0 = SequenceState(["a", "a", "b"], [1, 2]);
        var q1 = SequenceState(["b", "a", "a"], [2, 1]);
        var sequencePatch = ScalarSequenceHolder.Patch.Between(q0, q1);
        SemanticOracle.AssertEqual(q1, sequencePatch.Apply(q0), "sequence Between/apply");
        SemanticOracle.AssertEqual(
            q0,
            sequencePatch.Invert(q0).Apply(sequencePatch.Apply(q0)),
            "sequence invert round-trip"
        );
        SemanticOracle.AssertEqual(
            q1,
            sequencePatch.Compose(new ScalarSequenceHolder.Patch()).Apply(q0),
            "sequence compose identity"
        );

        // Keyed collections: the final key sequence is significant.
        var k0 = KeyedState(("a", "A", 0));
        var k1 = KeyedState(("a", "A2", 5), ("b", "B", 0));
        var k2 = KeyedState(("b", "B3", 5));
        var keyedFirst = KeyedServerHolder.Patch.Between(k0, k1);
        var keyedSecond = KeyedServerHolder.Patch.Between(k1, k2);
        SemanticOracle.AssertEqual(k1, keyedFirst.Apply(k0), "keyed Between/apply");
        SemanticOracle.AssertEqual(
            k2,
            keyedFirst.Compose(keyedSecond).Apply(k0),
            "keyed compose matches sequential apply"
        );
        SemanticOracle.AssertEqual(
            k0,
            keyedFirst
                .Compose(keyedSecond)
                .Invert(k0)
                .Apply(keyedFirst.Compose(keyedSecond).Apply(k0)),
            "keyed invert round-trip"
        );

        // Plain-class lists: atomic whole-list replacement.
        var before = ClassListState("a");
        var after = ClassListState("b", "c");
        var listPatch = ClassListHolder.Patch.Between(before, after);
        SemanticOracle.AssertEqual(after, listPatch.Apply(before), "plain-class list Between/apply");
        SemanticOracle.AssertEqual(
            before,
            listPatch.Invert(before).Apply(listPatch.Apply(before)),
            "plain-class list invert round-trip"
        );
    }

    [Test]
    public void CollectionComparerBoundariesHoldViaOracle()
    {
        // Sets use the explicitly-defined case-insensitive element policy.
        SemanticOracle
            .Differences(SetState("alpha"), SetState("ALPHA"))
            .ShouldBeEmpty();
        var ordinalSetDiffs = SemanticOracle.Differences(
            SetState("alpha"),
            SetState("ALPHA"),
            StringComparer.Ordinal
        );
        ordinalSetDiffs.ShouldNotBeEmpty();
        ordinalSetDiffs.ShouldContain(difference => difference.Contains("Values"));
        SemanticOracle
            .Differences(SetState("alpha", "beta"), SetState("alpha", "gamma"))
            .ShouldContain(difference => difference.Contains("Values"));

        // Dictionaries use the explicitly-defined case-insensitive key policy.
        SemanticOracle.Differences(DictState(("first", 1)), DictState(("FIRST", 1))).ShouldBeEmpty();
        var ordinalDictDiffs = SemanticOracle.Differences(
            DictState(("first", 1)),
            DictState(("FIRST", 1)),
            StringComparer.Ordinal
        );
        ordinalDictDiffs.ShouldNotBeEmpty();
        ordinalDictDiffs.ShouldContain(difference => difference.Contains("Values"));
        SemanticOracle
            .Differences(DictState(("first", 1)), DictState(("first", 2)))
            .ShouldContain(difference => difference.Contains("Values[\"first\"]"));

        // Sequences stay order-sensitive and duplicate-sensitive.
        SemanticOracle
            .Differences(SequenceState(["a", "b"], [1]), SequenceState(["b", "a"], [1]))
            .ShouldContain(difference => difference.Contains("Tags["));
        SemanticOracle
            .Differences(SequenceState(["a", "a", "b"], [1]), SequenceState(["a", "b"], [1]))
            .ShouldContain(difference => difference.Contains("Tags"));
        SemanticOracle
            .Differences(SequenceState(["a", "b"], [1]), SequenceState(["a", "b"], [1]))
            .ShouldBeEmpty();

        // Keyed collections treat reorder as a real difference.
        SemanticOracle
            .Differences(KeyedState(("a", "A", 0), ("b", "B", 0)), KeyedState(("b", "B", 0), ("a", "A", 0)))
            .ShouldContain(difference => difference.Contains("Items["));

        // Plain-class elements compare by structural value.
        SemanticOracle.Differences(ClassListState("a"), ClassListState("a")).ShouldBeEmpty();
        SemanticOracle
            .Differences(ClassListState("a"), ClassListState("b"))
            .ShouldContain(difference => difference.Contains("Items[0].Name"));
    }

    [Test]
    public void SemanticOracleReportsConcreteMembers()
    {
        var intact = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Enabled = Optional<bool>.Present(true),
                Label = Optional<string?>.Present("keep"),
                RetryCount = Optional<int>.Present(3),
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment
                    {
                        Host = Optional<string>.Present("host"),
                        Port = Optional<int>.Present(1),
                    }
                ),
                Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
            }
        );

        SemanticOracle.Differences(Optional<Settings.Fragment?>.Missing, intact).ShouldContain(d =>
            d.Contains("root")
        );
        SemanticOracle
            .Differences(Optional<Settings.Fragment?>.Present(null), intact)
            .ShouldContain(d => d.Contains("root"));

        var retryChanged = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Enabled = Optional<bool>.Present(true),
                Label = Optional<string?>.Present("keep"),
                RetryCount = Optional<int>.Present(4),
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment
                    {
                        Host = Optional<string>.Present("host"),
                        Port = Optional<int>.Present(1),
                    }
                ),
                Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
            }
        );
        var retryDiffs = SemanticOracle.Differences(intact, retryChanged);
        retryDiffs.Count.ShouldBe(1);
        retryDiffs[0].ShouldContain("RetryCount");

        var labelNulled = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Enabled = Optional<bool>.Present(true),
                Label = Optional<string?>.Present(null),
                RetryCount = Optional<int>.Present(3),
            }
        );
        var labelIntact = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Enabled = Optional<bool>.Present(true),
                Label = Optional<string?>.Present("keep"),
                RetryCount = Optional<int>.Present(3),
            }
        );
        SemanticOracle
            .Differences(labelIntact, labelNulled)
            .ShouldContain(difference => difference.Contains("Label"));

        var nestedChanged = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("other") }
                ),
            }
        );
        var nestedBasis = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment { Host = Optional<string>.Present("host") }
                ),
            }
        );
        SemanticOracle
            .Differences(nestedBasis, nestedChanged)
            .ShouldContain(difference => difference.Contains("Nested.Host"));

        var pluginsReordered = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Plugins = Optional<IReadOnlyList<string>>.Present(["b", "a"]),
            }
        );
        var pluginsBasis = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Plugins = Optional<IReadOnlyList<string>>.Present(["a", "b"]),
            }
        );
        SemanticOracle
            .Differences(pluginsBasis, pluginsReordered)
            .ShouldContain(difference => difference.Contains("Plugins["));

        SemanticOracle
            .Differences(SetState("a", "b"), SetState("a", "c"))
            .ShouldContain(difference => difference.Contains("Values"));
        SemanticOracle
            .Differences(DictState(("k", 1)), DictState(("k", 2)))
            .ShouldContain(difference => difference.Contains("Values[\"k\"]"));
    }

    [Test]
    public void OracleAgreesWithBetweenOnLawOutcomes()
    {
        // Cross-check: the independent oracle and Between concur on representative
        // law outcomes. The laws themselves are asserted via the oracle above; this
        // test only documents agreement on a small, cheap sample.
        var before = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { RetryCount = 1, Label = "a" })
        );
        var after = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { RetryCount = 2, Label = "b" })
        );
        var patch = Settings.Patch.Between(before, after);
        var applied = Apply(patch, before);
        SemanticOracle.AssertEqual(after, applied, "cross-check Between/apply");
        Settings.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();

        var first = new Settings.Patch { RetryCount = 12 };
        var second = new Settings.Patch { Enabled = false };
        var sequential = Apply(second, Apply(first, before));
        var composed = Apply(first.Compose(second), before);
        SemanticOracle.AssertEqual(sequential, composed, "cross-check compose");
        Settings.Patch.Between(sequential, composed).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void BetweenOfEqualStatesIsEmpty()
    {
        var state = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { RetryCount = 4 })
        );

        Settings.Patch.Between(state, state).IsEmpty.ShouldBeTrue();
        Settings
            .Patch.Between(
                Optional<Settings.Fragment?>.Missing,
                Optional<Settings.Fragment?>.Missing
            )
            .IsEmpty.ShouldBeTrue();
    }

    private static Optional<Settings.Fragment?> Apply(
        Settings.Patch patch,
        Optional<Settings.Fragment?> state
    ) => patch.Apply(state);

    private static Optional<SetSettings.Fragment?> SetState(params string[] values) =>
        Optional<SetSettings.Fragment?>.Present(
            new SetSettings.Fragment
            {
                Values = Optional<ISet<string>>.Present(
                    new HashSet<string>(values, StringComparer.OrdinalIgnoreCase)
                ),
            }
        );

    private static Optional<DictionarySettings.Fragment?> DictState(
        params (string Key, int Value)[] entries
    )
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            map[key] = value;
        }

        return Optional<DictionarySettings.Fragment?>.Present(
            new DictionarySettings.Fragment
            {
                Values = Optional<Dictionary<string, int>>.Present(map),
            }
        );
    }

    private static Optional<ScalarSequenceHolder.Fragment?> SequenceState(
        string[] tags,
        int[] numbers
    ) =>
        Optional<ScalarSequenceHolder.Fragment?>.Present(
            ScalarSequenceHolder.Fragment.From(
                new ScalarSequenceHolder { Tags = [.. tags], Numbers = [.. numbers] }
            )
        );

    private static Optional<ClassListHolder.Fragment?> ClassListState(params string[] names) =>
        Optional<ClassListHolder.Fragment?>.Present(
            ClassListHolder.Fragment.From(
                new ClassListHolder
                {
                    Items = names.Select(name => new ListChildItem { Name = name }).ToList(),
                }
            )
        );

    private static Optional<KeyedServerHolder.Fragment?> KeyedState(
        params (string Id, string Name, int Count)[] items
    ) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(
                new KeyedServerHolder
                {
                    Items = items
                        .Select(item => new KeyedServer
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Count = item.Count,
                        })
                        .ToList(),
                }
            )
        );

    private static void AssertSame(
        Optional<Settings.Fragment?> expected,
        Optional<Settings.Fragment?> actual,
        string? context = null
    )
    {
        SemanticOracle.AssertEqual(expected, actual, context);
    }
}
