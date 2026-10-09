using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Truly sparse keyed/dictionary ChangeSet storage and algebra (issue #103).</summary>
/// <remarks>
/// Canonical keyed/dict transitions retain only added(-&gt;after), removed(-&gt;before),
/// edited (nested) and key-only ordering – never full member before/after snapshots.
/// Per-key Compose/Rebase validate continuity only where keys interact, so disjoint
/// keys compose despite differing unrelated snapshots. Payload scales with the edit,
/// not the collection.
/// </remarks>
public sealed class SparseKeyedDictChangeSetTests
{
    private static KeyedServer S(string id, string? name = null, int count = 1) =>
        new()
        {
            Id = id,
            Name = name ?? id + "-original-" + new string('x', 50),
            Count = count,
        };

    private static Optional<KeyedServerHolder.Fragment?> KState(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
        );

    private static Optional<ScalarDictHolder.Fragment?> DState(Dictionary<string, int> scores) =>
        Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(new ScalarDictHolder { Scores = scores })
        );

    private static Optional<StructuralDictHolder.Fragment?> TState(
        Dictionary<string, KeyedServer> servers
    ) =>
        Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(new StructuralDictHolder { Servers = servers })
        );

    private static void AssertKeyedReplay(
        Optional<KeyedServerHolder.Fragment?> before,
        Optional<KeyedServerHolder.Fragment?> after,
        KeyedServerHolder.ChangeSet changes
    ) =>
        KeyedServerHolder
            .Patch.Between(changes.ToPatch().Apply(before), after)
            .IsEmpty.ShouldBeTrue();

    [Test]
    public void SingleEditedKeyDoesNotRetainUnrelatedValues()
    {
        var items = Enumerable.Range(0, 200).Select(i => S("k" + i)).ToArray();
        var edited = items
            .Select(s => new KeyedServer
            {
                Id = s.Id,
                Name = s.Name,
                Count = s.Count,
            })
            .ToArray();
        edited[7] = new KeyedServer
        {
            Id = "k7",
            Name = "EDITED-" + new string('y', 50),
            Count = 9,
        };
        var before = KState(items);
        var after = KState(edited);
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);

        changes.Items.IsChanged.ShouldBeTrue();
        changes.Items.Added.Count.ShouldBe(0);
        changes.Items.Removed.Count.ShouldBe(0);
        changes.Items.Edited.Count.ShouldBe(1);
        changes.Items.Edited.ContainsKey("k7").ShouldBeTrue();

        var json = JsonSerializer.Serialize(changes.ToPayload());
        // Unchanged element payloads must not be retained/serialized (wide names fail obviously).
        json.ShouldNotContain("k5-original-");
        json.ShouldNotContain("k150-original-");
        json.ShouldContain("EDITED-");
        // Order keys may be retained, but element values must not scale with the collection.
        var full = KeyedServerHolder.ChangeSet.Between(
            before,
            KState(
                items
                    .Select(s => new KeyedServer
                    {
                        Id = s.Id,
                        Name = "CHANGED-" + s.Id,
                        Count = 2,
                    })
                    .ToArray()
            )
        );
        var fullJson = JsonSerializer.Serialize(full.ToPayload());
        (json.Length * 3 < fullJson.Length).ShouldBeTrue(
            $"sparse single {json.Length} should scale vs full {fullJson.Length}"
        );

        var back = JsonSerializer.Deserialize<KeyedServerHolder.ChangePayload>(json)!.ToChangeSet();
        back.Items.Edited.ContainsKey("k7").ShouldBeTrue();
        back.Items.GetChange("k0").IsEmpty.ShouldBeTrue();
        AssertKeyedReplay(before, after, back);
    }

    [Test]
    public void SingleEditedDictEntryDoesNotRetainUnrelatedValues()
    {
        var beforeDict = Enumerable.Range(0, 200).ToDictionary(i => "key" + i, i => i);
        var afterDict = new Dictionary<string, int>(beforeDict) { ["key7"] = 9999 };
        var changes = ScalarDictHolder.ChangeSet.Between(DState(beforeDict), DState(afterDict));

        changes.Scores.IsChanged.ShouldBeTrue();
        changes.Scores.Added.Count.ShouldBe(0);
        changes.Scores.Removed.Count.ShouldBe(0);
        changes.Scores.Edited.Count.ShouldBe(1);

        var json = JsonSerializer.Serialize(changes.ToPayload());
        json.ShouldContain("9999");
        json.ShouldNotContain("key150");
        var back = JsonSerializer.Deserialize<ScalarDictHolder.ChangePayload>(json)!.ToChangeSet();
        back.Scores.Edited["key7"].ShouldBe(9999);
        back.Scores.GetChange("key0").IsEmpty.ShouldBeTrue();
        ScalarDictHolder
            .Patch.Between(back.ToPatch().Apply(DState(beforeDict)), DState(afterDict))
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void EnumerateChangesFlattensDictionaryEntries()
    {
        var before = DState(new Dictionary<string, int> { ["edit"] = 1, ["remove"] = 2 });
        var after = DState(new Dictionary<string, int> { ["edit"] = 3, ["add"] = 4 });
        var entries = ScalarDictHolder
            .ChangeSet.Between(before, after)
            .EnumerateChanges()
            .ToDictionary(static change => change.Path);

        entries["Scores[\"edit\"]"].Kind.ShouldBe(ScalarDictHolder.ChangeSet.ChangeKind.Changed);
        entries["Scores[\"edit\"]"].Before.Value.ShouldBe(1);
        entries["Scores[\"edit\"]"].After.Value.ShouldBe(3);
        entries["Scores[\"add\"]"].Kind.ShouldBe(ScalarDictHolder.ChangeSet.ChangeKind.Added);
        entries["Scores[\"remove\"]"].Kind.ShouldBe(ScalarDictHolder.ChangeSet.ChangeKind.Removed);
    }

    [Test]
    public void EnumeratePathsEscapeControlCharacters()
    {
        var tricky = "a\nb\tc\"d\\e\r\nf\x01g";
        var changes = ScalarDictHolder.ChangeSet.Between(
            DState(new Dictionary<string, int>()),
            DState(new Dictionary<string, int> { [tricky] = 1 })
        );
        changes.IsEmpty.ShouldBeFalse();

        var enumerated = changes.EnumerateChanges().ToList();
        enumerated.ShouldHaveSingleItem();
        // Canonical JSON escaping: control characters never appear raw.
        enumerated[0].Path.ShouldBe("Scores[\"a\\nb\\tc\\\"d\\\\e\\r\\nf\\u0001g\"]");
        foreach (var c in enumerated[0].Path)
        {
            (c < 0x20).ShouldBeFalse();
        }

        // Both enumeration surfaces agree, and simple keys are unchanged.
        changes.EnumerateChangedPaths().ShouldBe([enumerated[0].Path]);
        var segment = enumerated[0].Path.Substring("Scores[".Length);
        segment = segment.Substring(0, segment.Length - 1);
        JsonSerializer.Deserialize<string>(segment)!.ShouldBe(tricky);
    }

    [Test]
    public void EnumerateChangesFlattensNestedDictionaryValues()
    {
        var before = TState(
            new Dictionary<string, KeyedServer> { ["server"] = S("server", "before") }
        );
        var after = TState(
            new Dictionary<string, KeyedServer> { ["server"] = S("server", "after") }
        );
        var entries = StructuralDictHolder
            .ChangeSet.Between(before, after)
            .EnumerateChanges()
            .ToDictionary(static change => change.Path);

        entries["Servers[\"server\"].Name"].Before.Value.ShouldBe("before");
        entries["Servers[\"server\"].Name"].After.Value.ShouldBe("after");
    }

    [Test]
    public void KeyedAddRemoveEditReorderOnlyTransitions()
    {
        // Add-only.
        var add = KeyedServerHolder.ChangeSet.Between(KState(S("a")), KState(S("a"), S("b")));
        add.Items.GetChange("b").IsAdded.ShouldBeTrue();
        add.Items.GetChange("a").IsEmpty.ShouldBeTrue();
        AssertKeyedReplay(KState(S("a")), KState(S("a"), S("b")), add);
        add.Invert().Items.GetChange("b").IsRemoved.ShouldBeTrue();

        // Remove-only.
        var remove = KeyedServerHolder.ChangeSet.Between(KState(S("a"), S("b")), KState(S("b")));
        remove.Items.GetChange("a").IsRemoved.ShouldBeTrue();
        remove.Items.GetChange("b").IsEmpty.ShouldBeTrue();
        AssertKeyedReplay(KState(S("a"), S("b")), KState(S("b")), remove);

        // Edit-only (no order change).
        var edit = KeyedServerHolder.ChangeSet.Between(
            KState(S("a"), S("b")),
            KState(S("a", "A2"), S("b"))
        );
        edit.Items.GetChange("a").IsEdited.ShouldBeTrue();
        edit.Items.GetChange("a").IsReordered.ShouldBeFalse();
        edit.Items.OrderChanged.ShouldBeFalse();
        AssertKeyedReplay(KState(S("a"), S("b")), KState(S("a", "A2"), S("b")), edit);

        // Reorder-only.
        var reorder = KeyedServerHolder.ChangeSet.Between(
            KState(S("a"), S("b"), S("c")),
            KState(S("b"), S("a"), S("c"))
        );
        reorder.Items.OrderChanged.ShouldBeTrue();
        reorder.Items.GetChange("a").IsReordered.ShouldBeTrue();
        reorder.Items.GetChange("c").IsEmpty.ShouldBeTrue();
        reorder.Items.AfterOrder.ShouldBe(["b", "a", "c"]);
        AssertKeyedReplay(KState(S("a"), S("b"), S("c")), KState(S("b"), S("a"), S("c")), reorder);
        reorder.Invert().Items.AfterOrder.ShouldBe(["a", "b", "c"]);

        // Mixed.
        var mixed = KeyedServerHolder.ChangeSet.Between(
            KState(S("a"), S("b"), S("c")),
            KState(S("c", "C2"), S("d"))
        );
        mixed.Items.GetChange("d").IsAdded.ShouldBeTrue();
        mixed.Items.GetChange("a").IsRemoved.ShouldBeTrue();
        mixed.Items.GetChange("c").IsEdited.ShouldBeTrue();
        AssertKeyedReplay(KState(S("a"), S("b"), S("c")), KState(S("c", "C2"), S("d")), mixed);
        var mixedBack = JsonSerializer
            .Deserialize<KeyedServerHolder.ChangePayload>(
                JsonSerializer.Serialize(mixed.ToPayload())
            )!
            .ToChangeSet();
        AssertKeyedReplay(KState(S("a"), S("b"), S("c")), KState(S("c", "C2"), S("d")), mixedBack);
    }

    [Test]
    public void NestedKeyedElementChangeSets()
    {
        Optional<ClusterHolder.Fragment?> State(ClusterHolder m) =>
            Optional<ClusterHolder.Fragment?>.Present(ClusterHolder.Fragment.From(m));
        ClusterHolder Before() =>
            new()
            {
                Groups = new()
                {
                    new ServerGroup
                    {
                        Name = "g1",
                        Servers = new() { S("a"), S("b") },
                    },
                },
            };
        ClusterHolder After() =>
            new()
            {
                Groups = new()
                {
                    new ServerGroup
                    {
                        Name = "g1",
                        Servers = new()
                        {
                            new KeyedServer
                            {
                                Id = "a",
                                Name = "A2",
                                Count = 1,
                            },
                            S("b"),
                        },
                    },
                },
            };
        var changes = ClusterHolder.ChangeSet.Between(State(Before()), State(After()));
        changes.Groups.IsChanged.ShouldBeTrue();
        var back = JsonSerializer
            .Deserialize<ClusterHolder.ChangePayload>(
                JsonSerializer.Serialize(changes.ToPayload())
            )!
            .ToChangeSet();
        ClusterHolder
            .Patch.Between(back.ToPatch().Apply(State(Before())), State(After()))
            .IsEmpty.ShouldBeTrue();
        back.Invert().Invert().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void DisjointKeyedEditsComposeDespiteDifferingUnrelatedSnapshots()
    {
        // First edits A (B = B1 in its snapshots); second edits C (B = B2, different value).
        KeyedServer A0() =>
            new()
            {
                Id = "a",
                Name = "A0",
                Count = 1,
            };
        KeyedServer A1() =>
            new()
            {
                Id = "a",
                Name = "A1",
                Count = 1,
            };
        KeyedServer B1() =>
            new()
            {
                Id = "b",
                Name = "B1",
                Count = 1,
            };
        KeyedServer B2() =>
            new()
            {
                Id = "b",
                Name = "B2-DIFFERENT",
                Count = 1,
            };
        KeyedServer C0() =>
            new()
            {
                Id = "c",
                Name = "C0",
                Count = 1,
            };
        KeyedServer C1() =>
            new()
            {
                Id = "c",
                Name = "C1",
                Count = 1,
            };
        var first = KeyedServerHolder.ChangeSet.Between(
            KState(A0(), B1(), C0()),
            KState(A1(), B1(), C0())
        );
        var second = KeyedServerHolder.ChangeSet.Between(
            KState(A0(), B2(), C0()),
            KState(A0(), B2(), C1())
        );
        var composed = first.Compose(second);

        composed.Items.GetChange("a").IsEdited.ShouldBeTrue();
        composed.Items.GetChange("a").Edit.Name.After.Value.ShouldBe("A1");
        composed.Items.GetChange("c").IsEdited.ShouldBeTrue();
        composed.Items.GetChange("c").Edit.Name.After.Value.ShouldBe("C1");
        composed.Items.GetChange("b").IsEmpty.ShouldBeTrue();

        // Net replays both edits onto the first baseline (B stays B1, untouched).
        var merged = composed.ToPatch().Apply(KState(A0(), B1(), C0()));
        KeyedServerHolder.Patch.Between(merged, KState(A1(), B1(), C1())).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DisjointDictEditsComposeDespiteDifferingUnrelatedSnapshots()
    {
        var first = ScalarDictHolder.ChangeSet.Between(
            DState(
                new()
                {
                    ["a"] = 1,
                    ["b"] = 10,
                    ["c"] = 3,
                }
            ),
            DState(
                new()
                {
                    ["a"] = 2,
                    ["b"] = 10,
                    ["c"] = 3,
                }
            )
        );
        var second = ScalarDictHolder.ChangeSet.Between(
            DState(
                new()
                {
                    ["a"] = 1,
                    ["b"] = 99,
                    ["c"] = 3,
                }
            ),
            DState(
                new()
                {
                    ["a"] = 1,
                    ["b"] = 99,
                    ["c"] = 4,
                }
            )
        );
        var composed = first.Compose(second);

        composed.Scores.GetChange("a").IsEdited.ShouldBeTrue();
        composed.Scores.GetChange("c").IsEdited.ShouldBeTrue();
        composed.Scores.GetChange("b").IsEmpty.ShouldBeTrue();
        var merged = composed
            .ToPatch()
            .Apply(
                DState(
                    new()
                    {
                        ["a"] = 1,
                        ["b"] = 10,
                        ["c"] = 3,
                    }
                )
            );
        ScalarDictHolder
            .Patch.Between(
                merged,
                DState(
                    new()
                    {
                        ["a"] = 2,
                        ["b"] = 10,
                        ["c"] = 4,
                    }
                )
            )
            .IsEmpty.ShouldBeTrue();

        // Structural dictionary equivalent.
        var sFirst = StructuralDictHolder.ChangeSet.Between(
            TState(new() { ["x"] = S("s1", "Old"), ["y"] = S("s9", "Keep") }),
            TState(new() { ["x"] = S("s1", "New"), ["y"] = S("s9", "Keep") })
        );
        var sSecond = StructuralDictHolder.ChangeSet.Between(
            TState(new() { ["x"] = S("s1", "Old"), ["y"] = S("s9", "Keep") }),
            TState(
                new()
                {
                    ["x"] = S("s1", "Old"),
                    ["y"] = S("s9", "Keep"),
                    ["z"] = S("s2", "Z1"),
                }
            )
        );
        // Different snapshots share no edited keys (x vs z) with an extra unrelated entry each way.
        var sComposed = sFirst.Compose(sSecond);
        sComposed.Servers.GetChange("x").IsEdited.ShouldBeTrue();
        sComposed.Servers.GetChange("z").IsAdded.ShouldBeTrue();
    }

    [Test]
    public void ContiguousAndNonContiguousSameKeyComposition()
    {
        // Contiguous keyed edit chains.
        var s0 = KState(S("a", "A0"));
        var s1 = KState(S("a", "A1"));
        var s2 = KState(S("a", "A2"));
        var c1 = KeyedServerHolder.ChangeSet.Between(s0, s1);
        var c2 = KeyedServerHolder.ChangeSet.Between(s1, s2);
        var chained = c1.Compose(c2);
        chained.Items.GetChange("a").Edit.Name.After.Value.ShouldBe("A2");
        AssertKeyedReplay(s0, s2, chained);

        // Non-contiguous same key throws.
        var other = KeyedServerHolder.ChangeSet.Between(
            KState(S("a", "OTHER")),
            KState(S("a", "A9"))
        );
        Should.Throw<InvalidOperationException>(() => c1.Compose(other));

        // Contiguous scalar-dict chains and mismatches.
        var d0 = DState(new() { ["k"] = 1 });
        var d1 = DState(new() { ["k"] = 2 });
        var d2 = DState(new() { ["k"] = 3 });
        var e1 = ScalarDictHolder.ChangeSet.Between(d0, d1);
        var e2 = ScalarDictHolder.ChangeSet.Between(d1, d2);
        e1.Compose(e2).Scores.GetChange("k").After.Value.ShouldBe(3);
        var eBroken = ScalarDictHolder.ChangeSet.Between(
            DState(new() { ["k"] = 9 }),
            DState(new() { ["k"] = 10 })
        );
        Should.Throw<InvalidOperationException>(() => e1.Compose(eBroken));

        // Add-then-remove normalizes to empty; remove-then-add becomes edit.
        var add = KeyedServerHolder.ChangeSet.Between(KState(S("a")), KState(S("a"), S("b")));
        var del = KeyedServerHolder.ChangeSet.Between(KState(S("a"), S("b")), KState(S("a")));
        add.Compose(del).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedMembershipOrderInteractionComposes()
    {
        KeyedServer K(string id) => S(id);
        var s0 = KState(K("a"), K("b"), K("c"));
        var s1 = KState(K("b"), K("a"), K("c"));
        var s2 = KState(
            K("b"),
            new KeyedServer
            {
                Id = "a",
                Name = "A2",
                Count = 1,
            },
            K("c")
        );
        var composed = KeyedServerHolder
            .ChangeSet.Between(s0, s1)
            .Compose(KeyedServerHolder.ChangeSet.Between(s1, s2));

        composed.Items.OrderChanged.ShouldBeTrue();
        composed.Items.Edited.ContainsKey("a").ShouldBeTrue();
        composed.Items.AfterOrder.ShouldBe(["b", "a", "c"]);
        AssertKeyedReplay(s0, s2, composed);
    }

    [Test]
    public void RebaseWithUnrelatedConcurrentChanges()
    {
        // Keyed: local edits A, concurrent adds C.
        var before = KState(S("a", "A0"), S("b"));
        var edited = KState(S("a", "A1"), S("b"));
        var current = KState(S("a", "A0"), S("b"), S("c"));
        var rebased = KeyedServerHolder.ChangeSet.Between(before, edited).RebaseOnto(current);
        rebased.HasConflicts.ShouldBeFalse();
        KeyedServerHolder
            .Patch.Between(
                rebased.Rebased.ToPatch().Apply(current),
                KState(S("a", "A1"), S("b"), S("c"))
            )
            .IsEmpty.ShouldBeTrue();

        // Already applied.
        var already = KeyedServerHolder.ChangeSet.Between(before, edited).RebaseOnto(edited);
        already.HasConflicts.ShouldBeFalse();
        already.Rebased.IsEmpty.ShouldBeTrue();

        // Same-key conflict keeps clean keys.
        var multi = KeyedServerHolder.ChangeSet.Between(
            KState(S("a", "A0"), S("b", "B0")),
            KState(S("a", "A1"), S("b", "B1"))
        );
        var conflicted = multi.RebaseOnto(KState(S("a", "OTHER"), S("b", "B0")));
        conflicted.HasConflicts.ShouldBeTrue();
        conflicted.Rebased.Items.GetChange("b").IsEdited.ShouldBeTrue();
        conflicted.Rebased.Items.GetChange("b").Edit.Name.After.Value.ShouldBe("B1");

        // Dictionary: unrelated concurrent add merges; same-key conflict isolates.
        var dChanges = ScalarDictHolder.ChangeSet.Between(
            DState(new() { ["a"] = 1 }),
            DState(new() { ["a"] = 2 })
        );
        var dMerged = dChanges.RebaseOnto(DState(new() { ["a"] = 1, ["c"] = 9 }));
        dMerged.HasConflicts.ShouldBeFalse();
        ScalarDictHolder
            .Patch.Between(
                dMerged.Rebased.ToPatch().Apply(DState(new() { ["a"] = 1, ["c"] = 9 })),
                DState(new() { ["a"] = 2, ["c"] = 9 })
            )
            .IsEmpty.ShouldBeTrue();

        var dMulti = ScalarDictHolder.ChangeSet.Between(
            DState(new() { ["a"] = 1, ["b"] = 1 }),
            DState(new() { ["a"] = 2, ["b"] = 2 })
        );
        var dConflicted = dMulti.RebaseOnto(DState(new() { ["a"] = 99, ["b"] = 1 }));
        dConflicted.HasConflicts.ShouldBeTrue();
        dConflicted.Rebased.Scores.GetChange("b").After.Value.ShouldBe(2);
    }

    [Test]
    public void InvertAndToPatchForSparseTransitions()
    {
        var before = KState(S("a"), S("b"), S("c"));
        var after = KState(S("c", "C2"), S("b"), S("d"));
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);
        var inverted = changes.Invert();
        inverted.Items.GetChange("d").IsRemoved.ShouldBeTrue();
        inverted.Items.GetChange("a").IsAdded.ShouldBeTrue();
        inverted.Items.OrderChanged.ShouldBe(changes.Items.OrderChanged);
        KeyedServerHolder
            .Patch.Between(inverted.ToPatch().Apply(after), before)
            .IsEmpty.ShouldBeTrue();
        changes.Invert().Invert().Items.AfterOrder.ShouldBe(changes.Items.AfterOrder.ToArray());

        var dBefore = DState(new() { ["a"] = 1, ["b"] = 2 });
        var dAfter = DState(new() { ["b"] = 3, ["c"] = 4 });
        var dChanges = ScalarDictHolder.ChangeSet.Between(dBefore, dAfter);
        var dInverted = dChanges.Invert();
        dInverted.Scores.GetChange("c").IsRemoved.ShouldBeTrue();
        dInverted.Scores.GetChange("a").IsAdded.ShouldBeTrue();
        ScalarDictHolder
            .Patch.Between(dInverted.ToPatch().Apply(dAfter), dBefore)
            .IsEmpty.ShouldBeTrue();
    }
}
