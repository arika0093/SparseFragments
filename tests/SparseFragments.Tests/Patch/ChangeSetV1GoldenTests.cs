using System.Text.Json;

namespace SparseFragments.Tests;

/// <summary>ChangeSet JSON v1 format compatibility boundary (issue #105).</summary>
/// <remarks>
/// Writer comparisons are structural (object order ignored, array order significant);
/// reader checks verify semantic replay (ToPatch/Invert/Compose/Rebase).
/// </remarks>
public sealed class ChangeSetV1GoldenTests
{
    // Committed v1 fixtures.
    private const string EmptyV1 = """{"version":1,"changes":{}}""";

    private const string WholeV1 =
        """{"version":1,"changes":{"$whole":{"before":{"state":"missing"},"after":{"state":"value","value":{"Label":"Alice"}}}}}""";

    private const string ScalarV1 =
        """{"version":1,"changes":{"Label":{"before":{"state":"value","value":"Alice"},"after":{"state":"value","value":"Bob"}}}}""";

    private const string PresenceMissingToValueV1 =
        """{"version":1,"changes":{"Label":{"before":{"state":"missing"},"after":{"state":"value","value":"v"}}}}""";

    private const string PresenceValueToMissingV1 =
        """{"version":1,"changes":{"Label":{"before":{"state":"value","value":"v"},"after":{"state":"missing"}}}}""";

    private const string PresenceNullToValueV1 =
        """{"version":1,"changes":{"Label":{"before":{"state":"null"},"after":{"state":"value","value":"v"}}}}""";

    private const string MissingToNullWholeV1 =
        """{"version":1,"changes":{"$whole":{"before":{"state":"missing"},"after":{"state":"null"}}}}""";

    private const string NestedV1 =
        """{"version":1,"changes":{"Nested":{"Host":{"before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}}}}}""";

    private const string KeyedAddV1 =
        """{"version":1,"changes":{"Items":{"items":[{"key":"b","kind":"add","after":{"Id":"b","Name":"B","Count":1},"beforeIndex":-1,"afterIndex":1}],"beforeOrder":["a"],"afterOrder":["a","b"]}}}""";

    private const string KeyedRemoveV1 =
        """{"version":1,"changes":{"Items":{"items":[{"key":"a","kind":"remove","before":{"Id":"a","Name":"A","Count":1},"beforeIndex":0,"afterIndex":-1}],"beforeOrder":["a","b"],"afterOrder":["b"]}}}""";

    private const string KeyedEditV1 =
        """{"version":1,"changes":{"Items":{"items":[{"key":"a","kind":"edit","before":{"Id":"a","Name":"A0","Count":1},"after":{"Id":"a","Name":"A1","Count":1},"beforeIndex":0,"afterIndex":0}],"beforeOrder":["a","b"],"afterOrder":["a","b"]}}}""";

    private const string KeyedReorderV1 =
        """{"version":1,"changes":{"Items":{"items":[{"key":"b","kind":"reorder","before":{"Id":"b","Name":"B","Count":1},"after":{"Id":"b","Name":"B","Count":1},"beforeIndex":1,"afterIndex":0},{"key":"a","kind":"reorder","before":{"Id":"a","Name":"A","Count":1},"after":{"Id":"a","Name":"A","Count":1},"beforeIndex":0,"afterIndex":1}],"beforeOrder":["a","b","c"],"afterOrder":["b","a","c"]}}}""";

    private const string KeyedMixedV1 =
        """{"version":1,"changes":{"Items":{"items":[{"key":"c","kind":"edit","before":{"Id":"c","Name":"C","Count":1},"after":{"Id":"c","Name":"C2","Count":1},"beforeIndex":2,"afterIndex":0},{"key":"d","kind":"add","after":{"Id":"d","Name":"D","Count":1},"beforeIndex":-1,"afterIndex":1},{"key":"a","kind":"remove","before":{"Id":"a","Name":"A","Count":1},"beforeIndex":0,"afterIndex":-1},{"key":"b","kind":"remove","before":{"Id":"b","Name":"B","Count":1},"beforeIndex":1,"afterIndex":-1}],"beforeOrder":["a","b","c"],"afterOrder":["c","d"]}}}""";

    private const string DictV1 =
        """{"version":1,"changes":{"Scores":{"items":[{"key":"c","kind":"add","after":4},{"key":"a","kind":"remove","before":1},{"key":"b","kind":"edit","before":2,"after":3}]}}}""";

    private const string CompositeKeyV1 =
        """{"version":1,"changes":{"Items":{"items":[{"key":["t2","a"],"kind":"add","after":{"TenantId":"t2","Id":"a","Name":"N2"},"beforeIndex":-1,"afterIndex":1}],"beforeOrder":[["t1","a"]],"afterOrder":[["t1","a"],["t2","a"]]}}}""";

    private const string JsonPropertyNameV1 =
        """{"version":1,"changes":{"customName":{"before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}},"a/b":{"before":{"state":"value","value":1},"after":{"state":"value","value":3}}}}""";

    private const string NamingPolicyV1 =
        """{"version":1,"changes":{"label":{"before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}}}}""";

    private static Optional<Settings.Fragment?> SettingsState(Settings.Fragment f) =>
        Optional<Settings.Fragment?>.Present(f);

    private static KeyedServer Server(string id, string name, int count = 1) =>
        new() { Id = id, Name = name, Count = count };

    private static Optional<KeyedServerHolder.Fragment?> KeyedState(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
        );

    private static Optional<ScalarDictHolder.Fragment?> DictState(Dictionary<string, int> scores) =>
        Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(new ScalarDictHolder { Scores = scores })
        );

    private static void AssertStructuralEqual(string expected, string actual)
    {
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);
        if (!JsonElementStructuralEqual(e.RootElement, a.RootElement))
        {
            throw new ShouldAssertException($"JSON structurally differs.\nExpected: {expected}\nActual: {actual}");
        }
    }

    private static bool JsonElementStructuralEqual(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var eProps = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                var aProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                if (eProps.Count != aProps.Count)
                {
                    return false;
                }

                foreach (var (name, eValue) in eProps)
                {
                    if (!aProps.TryGetValue(name, out var aValue))
                    {
                        return false;
                    }

                    if (!JsonElementStructuralEqual(eValue, aValue))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Array:
                var eItems = expected.EnumerateArray().ToArray();
                var aItems = actual.EnumerateArray().ToArray();
                if (eItems.Length != aItems.Length)
                {
                    return false;
                }

                for (var i = 0; i < eItems.Length; i++)
                {
                    if (!JsonElementStructuralEqual(eItems[i], aItems[i]))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Number:
                return expected.GetRawText() == actual.GetRawText();
            default:
                return expected.GetRawText() == actual.GetRawText();
        }
    }

    [Test]
    public void Writer_EmitsVersionedEnvelopes()
    {
        var empty = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Missing,
            Optional<Settings.Fragment?>.Missing
        );
        AssertStructuralEqual(EmptyV1, JsonSerializer.Serialize(empty));

        var whole = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Missing,
            SettingsState(new Settings.Fragment { Label = Optional<string?>.Present("Alice") })
        );
        AssertStructuralEqual(WholeV1, JsonSerializer.Serialize(whole));

        var scalar = Settings.ChangeSet.Between(
            SettingsState(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("Alice"),
                    RetryCount = Optional<int>.Present(1),
                }
            ),
            SettingsState(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("Bob"),
                    RetryCount = Optional<int>.Present(1),
                }
            )
        );
        AssertStructuralEqual(ScalarV1, JsonSerializer.Serialize(scalar));

        var nested = Settings.ChangeSet.Between(
            SettingsState(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("keep"),
                    Nested = Optional<Nested.Fragment?>.Present(
                        new Nested.Fragment
                        {
                            Host = Optional<string>.Present("a"),
                            Port = Optional<int>.Present(1),
                        }
                    ),
                }
            ),
            SettingsState(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("keep"),
                    Nested = Optional<Nested.Fragment?>.Present(
                        new Nested.Fragment
                        {
                            Host = Optional<string>.Present("b"),
                            Port = Optional<int>.Present(1),
                        }
                    ),
                }
            )
        );
        var nestedJson = JsonSerializer.Serialize(nested);
        AssertStructuralEqual(NestedV1, nestedJson);
        // Nested bodies must not carry nested envelopes.
        nestedJson.ShouldNotContain("\"Nested\":{\"version\"");
        nestedJson.ShouldNotContain("\"Nested\":{\"changes\"");
    }

    [Test]
    public void Writer_EmitsPresenceTransitions()
    {
        Optional<Settings.Fragment?> St(string? label, bool present, bool isNull = false) =>
            SettingsState(
                new Settings.Fragment
                {
                    Label = present
                        ? isNull
                            ? Optional<string?>.Present(null)
                            : Optional<string?>.Present(label)
                        : Optional<string?>.Missing,
                    RetryCount = Optional<int>.Present(1),
                }
            );
        AssertStructuralEqual(
            PresenceMissingToValueV1,
            JsonSerializer.Serialize(Settings.ChangeSet.Between(St(null, false), St("v", true)))
        );
        AssertStructuralEqual(
            PresenceValueToMissingV1,
            JsonSerializer.Serialize(Settings.ChangeSet.Between(St("v", true), St(null, false)))
        );
        AssertStructuralEqual(
            PresenceNullToValueV1,
            JsonSerializer.Serialize(Settings.ChangeSet.Between(St(null, true, true), St("v", true)))
        );
        AssertStructuralEqual(
            MissingToNullWholeV1,
            JsonSerializer.Serialize(
                Settings.ChangeSet.Between(
                    Optional<Settings.Fragment?>.Missing,
                    Optional<Settings.Fragment?>.Present(null)
                )
            )
        );
    }

    [Test]
    public void Writer_EmitsKeyedTransitions()
    {
        AssertStructuralEqual(
            KeyedAddV1,
            JsonSerializer.Serialize(
                KeyedServerHolder.ChangeSet.Between(
                    KeyedState(Server("a", "A")),
                    KeyedState(Server("a", "A"), Server("b", "B"))
                )
            )
        );
        AssertStructuralEqual(
            KeyedRemoveV1,
            JsonSerializer.Serialize(
                KeyedServerHolder.ChangeSet.Between(
                    KeyedState(Server("a", "A"), Server("b", "B")),
                    KeyedState(Server("b", "B"))
                )
            )
        );
        AssertStructuralEqual(
            KeyedEditV1,
            JsonSerializer.Serialize(
                KeyedServerHolder.ChangeSet.Between(
                    KeyedState(Server("a", "A0"), Server("b", "B")),
                    KeyedState(Server("a", "A1"), Server("b", "B"))
                )
            )
        );
        AssertStructuralEqual(
            KeyedReorderV1,
            JsonSerializer.Serialize(
                KeyedServerHolder.ChangeSet.Between(
                    KeyedState(Server("a", "A"), Server("b", "B"), Server("c", "C")),
                    KeyedState(Server("b", "B"), Server("a", "A"), Server("c", "C"))
                )
            )
        );
        AssertStructuralEqual(
            KeyedMixedV1,
            JsonSerializer.Serialize(
                KeyedServerHolder.ChangeSet.Between(
                    KeyedState(Server("a", "A"), Server("b", "B"), Server("c", "C")),
                    KeyedState(Server("c", "C2"), Server("d", "D"))
                )
            )
        );
    }

    [Test]
    public void Writer_EmitsDictionaryCompositeNamingFixtures()
    {
        AssertStructuralEqual(
            DictV1,
            JsonSerializer.Serialize(
                ScalarDictHolder.ChangeSet.Between(
                    DictState(new() { ["a"] = 1, ["b"] = 2 }),
                    DictState(new() { ["b"] = 3, ["c"] = 4 })
                )
            )
        );

        Optional<CompositeServerHolder.Fragment?> CState(CompositeServerHolder m) =>
            Optional<CompositeServerHolder.Fragment?>.Present(
                CompositeServerHolder.Fragment.From(m)
            );
        AssertStructuralEqual(
            CompositeKeyV1,
            JsonSerializer.Serialize(
                CompositeServerHolder.ChangeSet.Between(
                    CState(
                        new CompositeServerHolder
                        {
                            Items = [new CompositeServer { TenantId = "t1", Id = "a", Name = "N1" }],
                        }
                    ),
                    CState(
                        new CompositeServerHolder
                        {
                            Items =
                            [
                                new CompositeServer { TenantId = "t1", Id = "a", Name = "N1" },
                                new CompositeServer { TenantId = "t2", Id = "a", Name = "N2" },
                            ],
                        }
                    )
                )
            )
        );

        Optional<NamingWidget.Fragment?> NState(NamingWidget m) =>
            Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(m));
        AssertStructuralEqual(
            JsonPropertyNameV1,
            JsonSerializer.Serialize(
                NamingWidget.ChangeSet.Between(
                    NState(new NamingWidget { Value = "a", Slash = 1, Plain = 2 }),
                    NState(new NamingWidget { Value = "b", Slash = 3, Plain = 2 })
                )
            )
        );

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var camelJson = JsonSerializer.Serialize(
            Settings.ChangeSet.Between(
                SettingsState(
                    new Settings.Fragment
                    {
                        Label = Optional<string?>.Present("a"),
                        RetryCount = Optional<int>.Present(1),
                    }
                ),
                SettingsState(
                    new Settings.Fragment
                    {
                        Label = Optional<string?>.Present("b"),
                        RetryCount = Optional<int>.Present(1),
                    }
                )
            ),
            camel
        );
        AssertStructuralEqual(NamingPolicyV1, camelJson);
        // Envelope names stay exact under a naming policy.
        camelJson.ShouldContain("\"version\":1");
        camelJson.ShouldContain("\"changes\":");
    }

    [Test]
    public void Reader_FixturesReplaySemantically()
    {
        var empty = JsonSerializer.Deserialize<Settings.ChangeSet>(EmptyV1)!;
        empty.IsEmpty.ShouldBeTrue();

        var scalarBefore = SettingsState(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("Alice"),
                RetryCount = Optional<int>.Present(1),
            }
        );
        var scalarAfter = SettingsState(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("Bob"),
                RetryCount = Optional<int>.Present(1),
            }
        );
        var scalar = JsonSerializer.Deserialize<Settings.ChangeSet>(ScalarV1)!;
        Settings.Patch.Between(scalar.ToPatch().Apply(scalarBefore), scalarAfter).IsEmpty.ShouldBeTrue();
        scalar.Invert().Invert().IsEmpty.ShouldBeFalse();
        Settings.Patch
            .Between(scalar.Invert().ToPatch().Apply(scalarAfter), scalarBefore)
            .IsEmpty.ShouldBeTrue();

        var nestedBefore = SettingsState(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("keep"),
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment
                    {
                        Host = Optional<string>.Present("a"),
                        Port = Optional<int>.Present(1),
                    }
                ),
            }
        );
        var nestedAfter = SettingsState(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("keep"),
                Nested = Optional<Nested.Fragment?>.Present(
                    new Nested.Fragment
                    {
                        Host = Optional<string>.Present("b"),
                        Port = Optional<int>.Present(1),
                    }
                ),
            }
        );
        var nested = JsonSerializer.Deserialize<Settings.ChangeSet>(NestedV1)!;
        nested.Nested.Host.After.Value.ShouldBe("b");
        Settings.Patch.Between(nested.ToPatch().Apply(nestedBefore), nestedAfter).IsEmpty.ShouldBeTrue();

        var whole = JsonSerializer.Deserialize<Settings.ChangeSet>(WholeV1)!;
        Settings.Patch
            .Between(
                whole.ToPatch().Apply(Optional<Settings.Fragment?>.Missing),
                SettingsState(new Settings.Fragment { Label = Optional<string?>.Present("Alice") })
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Reader_KeyedDictFixturesSupportAlgebra()
    {
        var addBefore = KeyedState(Server("a", "A"));
        var addAfter = KeyedState(Server("a", "A"), Server("b", "B"));
        var add = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSet>(KeyedAddV1)!;
        add.Items.GetChange("b").IsAdded.ShouldBeTrue();
        KeyedServerHolder.Patch.Between(add.ToPatch().Apply(addBefore), addAfter).IsEmpty.ShouldBeTrue();

        var remove = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSet>(KeyedRemoveV1)!;
        remove.Items.GetChange("a").IsRemoved.ShouldBeTrue();
        KeyedServerHolder.Patch
            .Between(
                remove.ToPatch().Apply(KeyedState(Server("a", "A"), Server("b", "B"))),
                KeyedState(Server("b", "B"))
            )
            .IsEmpty.ShouldBeTrue();

        var edit = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSet>(KeyedEditV1)!;
        edit.Items.GetChange("a").IsEdited.ShouldBeTrue();
        edit.Invert().Items.GetChange("a").IsEdited.ShouldBeTrue();

        var reorder = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSet>(KeyedReorderV1)!;
        reorder.Items.OrderChanged.ShouldBeTrue();
        reorder.Items.AfterOrder.ShouldBe(["b", "a", "c"]);

        var mixed = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSet>(KeyedMixedV1)!;
        mixed.Items.GetChange("d").IsAdded.ShouldBeTrue();
        mixed.Items.GetChange("a").IsRemoved.ShouldBeTrue();
        mixed.Items.GetChange("c").IsEdited.ShouldBeTrue();
        KeyedServerHolder.Patch
            .Between(
                mixed.ToPatch().Apply(KeyedState(Server("a", "A"), Server("b", "B"), Server("c", "C"))),
                KeyedState(Server("c", "C2"), Server("d", "D"))
            )
            .IsEmpty.ShouldBeTrue();

        var dict = JsonSerializer.Deserialize<ScalarDictHolder.ChangeSet>(DictV1)!;
        dict.Scores.GetChange("c").IsAdded.ShouldBeTrue();
        dict.Scores.GetChange("a").IsRemoved.ShouldBeTrue();
        dict.Scores.GetChange("b").IsEdited.ShouldBeTrue();
        ScalarDictHolder.Patch
            .Between(
                dict.ToPatch().Apply(DictState(new() { ["a"] = 1, ["b"] = 2 })),
                DictState(new() { ["b"] = 3, ["c"] = 4 })
            )
            .IsEmpty.ShouldBeTrue();

        // Compose contiguous keyed fixtures.
        var s0 = KeyedState(Server("a", "A0"));
        var s1 = KeyedState(Server("a", "A1"));
        var s2 = KeyedState(Server("a", "A2"));
        var c1 = KeyedServerHolder.ChangeSet.Between(s0, s1);
        var c2 = JsonSerializer.Deserialize<KeyedServerHolder.ChangeSet>(
            JsonSerializer.Serialize(KeyedServerHolder.ChangeSet.Between(s1, s2))
        )!;
        c1.Compose(c2).Items.GetChange("a").Edit.Name.After.Value.ShouldBe("A2");

        // Rebase a scalar fixture onto concurrent state.
        var @base = SettingsState(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("Alice"),
                RetryCount = Optional<int>.Present(20),
            }
        );
        var edited = SettingsState(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("Alice"),
                RetryCount = Optional<int>.Present(21),
            }
        );
        var current = SettingsState(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("Bob"),
                RetryCount = Optional<int>.Present(20),
            }
        );
        var changes = Settings.ChangeSet.Between(@base, edited);
        var roundTripped = JsonSerializer.Deserialize<Settings.ChangeSet>(
            JsonSerializer.Serialize(changes)
        )!;
        var rebased = roundTripped.RebaseOnto(current);
        rebased.HasConflicts.ShouldBeFalse();
        Settings.Patch
            .Between(
                rebased.Patch.ToPatch().Apply(current),
                SettingsState(
                    new Settings.Fragment
                    {
                        Label = Optional<string?>.Present("Bob"),
                        RetryCount = Optional<int>.Present(21),
                    }
                )
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Reader_NamingFixturesReplay()
    {
        Optional<NamingWidget.Fragment?> NState(NamingWidget m) =>
            Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(m));
        var before = NState(new NamingWidget { Value = "a", Slash = 1, Plain = 2 });
        var after = NState(new NamingWidget { Value = "b", Slash = 3, Plain = 2 });
        var back = JsonSerializer.Deserialize<NamingWidget.ChangeSet>(JsonPropertyNameV1)!;
        NamingWidget.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var camelBack = JsonSerializer.Deserialize<Settings.ChangeSet>(NamingPolicyV1, camel)!;
        Settings.Patch
            .Between(
                camelBack.ToPatch().Apply(
                    SettingsState(
                        new Settings.Fragment
                        {
                            Label = Optional<string?>.Present("a"),
                            RetryCount = Optional<int>.Present(1),
                        }
                    )
                ),
                SettingsState(
                    new Settings.Fragment
                    {
                        Label = Optional<string?>.Present("b"),
                        RetryCount = Optional<int>.Present(1),
                    }
                )
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Reader_EnvelopeIsStrictAndOrderIndependent()
    {
        // Order-independent.
        var reordered =
            """{"changes":{"Label":{"before":{"state":"value","value":"Alice"},"after":{"state":"value","value":"Bob"}}},"version":1}""";
        var back = JsonSerializer.Deserialize<Settings.ChangeSet>(reordered)!;
        back.Label.After.Value.ShouldBe("Bob");

        // Missing/duplicate/malformed/unsupported/unknown envelope shapes fail.
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Settings.ChangeSet>("""{}"""));
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":1}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":1,"version":1,"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":1,"changes":{},"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":"1","changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":1.0,"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":true,"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":null,"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":0,"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":2,"changes":{}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"version":1,"changes":{},"extra":{}}""")
        );
        // Old unversioned body must not be accepted as a document.
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"Label":{"before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}}}""")
        );
        Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Settings.ChangeSet>("""{"before":{"state":"missing"}}""")
        );
    }
}
