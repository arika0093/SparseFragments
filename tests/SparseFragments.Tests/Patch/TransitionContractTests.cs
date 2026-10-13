using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments;
using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>Minimal external consumer of the read-only transition contracts (issue #223).</summary>
/// <remarks>
/// The <see cref="ExternalConsumer"/> helpers below program only against the
/// runtime contracts (<c>ISparseChangeSet</c>, <c>ISparseValueChange{T}</c>,
/// <c>ISparseKeyedChange{TKey, TElement, TElementChange}</c>, ...): no
/// generated nested CLR type name appears in their signatures. The tests cover
/// scalar presence, nested edits, composite keys, pure reorder, dictionaries,
/// and collection-scoped temporary identity.
/// </remarks>
public sealed class TransitionContractTests
{
    /// <summary>Stands in for a separately compiled consumer (for example a persistence mapper).</summary>
    private static class ExternalConsumer
    {
        public static List<string> SummarizeKeyed<TKey, TElement, TElementChange>(
            ISparseKeyedChange<TKey, TElement, TElementChange> transition
        )
            where TKey : notnull
        {
            var summary = new List<string>();
            foreach (var item in transition.EnumerateItems())
            {
                var flags = string.Concat(
                    item.IsAdded ? "added" : string.Empty,
                    item.IsRemoved ? "removed" : string.Empty,
                    item.IsEdited ? "edited" : string.Empty,
                    item.IsReordered ? "reordered" : string.Empty
                );
                summary.Add($"{item.Key}:{flags}:{item.BeforeIndex}->{item.AfterIndex}");
            }
            return summary;
        }

        public static List<string> SummarizeRecords(ISparseChangeSet changes) =>
            changes
                .EnumerateChangeRecords()
                .Select(static record => record.Kind + "@" + record.PathText)
                .ToList();
    }

    private static Optional<Settings.Fragment?> Present(Settings.Fragment fragment) =>
        Optional<Settings.Fragment?>.Present(fragment);

    private static Settings.Fragment MakeSettings(
        Optional<string?> label,
        Optional<int> retry,
        Optional<Nested.Fragment?> nested
    ) =>
        new()
        {
            Label = label,
            RetryCount = retry,
            Nested = nested,
        };

    private static Optional<Nested.Fragment?> NestedState(string host) =>
        Optional<Nested.Fragment?>.Present(
            new Nested.Fragment { Host = Optional<string>.Present(host) }
        );

    [Test]
    public void ScalarPresenceDistinguishesMissingNullAndValue()
    {
        var before = Present(
            MakeSettings(
                Optional<string?>.Present("before"),
                Optional<int>.Present(1),
                Optional<Nested.Fragment?>.Missing
            )
        );
        var missing = Present(
            MakeSettings(
                Optional<string?>.Missing,
                Optional<int>.Present(1),
                Optional<Nested.Fragment?>.Missing
            )
        );
        var presentNull = Present(
            MakeSettings(
                Optional<string?>.Present(null),
                Optional<int>.Present(1),
                Optional<Nested.Fragment?>.Missing
            )
        );

        ISparseChangeSet<Settings> toMissing = Settings.ChangeSet.Between(before, missing);
        toMissing.IsChanged.ShouldBeTrue();
        toMissing.TryGetValueChange<string?>("Label", out var toMissingLabel).ShouldBeTrue();
        toMissingLabel.ShouldNotBeNull();
        toMissingLabel!.IsChanged.ShouldBeTrue();
        toMissingLabel.Before.IsPresent.ShouldBeTrue();
        toMissingLabel.Before.Value.ShouldBe("before");
        toMissingLabel.After.IsPresent.ShouldBeFalse();

        ISparseChangeSet toNull = Settings.ChangeSet.Between(missing, presentNull);
        toNull.TryGetValueChange<string?>("Label", out var toNullLabel).ShouldBeTrue();
        toNullLabel.ShouldNotBeNull();
        toNullLabel!.IsChanged.ShouldBeTrue();
        toNullLabel.Before.IsPresent.ShouldBeFalse();
        toNullLabel.After.IsPresent.ShouldBeTrue();
        toNullLabel.After.Value.ShouldBeNull();
    }

    [Test]
    public void UnchangedScalarReportsNoChangeAndRejectsMismatches()
    {
        var before = Present(
            MakeSettings(
                Optional<string?>.Present("same"),
                Optional<int>.Present(1),
                Optional<Nested.Fragment?>.Missing
            )
        );
        ISparseChangeSet changes = Settings.ChangeSet.Between(before, before);
        changes.IsEmpty.ShouldBeTrue();
        changes.IsChanged.ShouldBeFalse();

        changes.TryGetValueChange<string?>("Label", out var label).ShouldBeTrue();
        label.ShouldNotBeNull();
        label!.IsChanged.ShouldBeFalse();

        changes.TryGetValueChange<int>("Label", out _).ShouldBeFalse();
        changes.TryGetValueChange<string?>("Absent", out _).ShouldBeFalse();
        changes.TryGetNestedChange("Label", out _).ShouldBeFalse();
    }

    [Test]
    public void NestedEditsRecurseThroughTheContract()
    {
        var before = Present(
            MakeSettings(
                Optional<string?>.Present("same"),
                Optional<int>.Present(1),
                NestedState("host-before")
            )
        );
        var after = Present(
            MakeSettings(
                Optional<string?>.Present("same"),
                Optional<int>.Present(1),
                NestedState("host-after")
            )
        );
        ISparseChangeSet changes = Settings.ChangeSet.Between(before, after);
        changes.IsChanged.ShouldBeTrue();

        changes.TryGetNestedChange("Nested", out var nested).ShouldBeTrue();
        nested.ShouldNotBeNull();
        nested!.GetType().GetInterfaces().ShouldContain(typeof(ISparseChangeSet<Nested>));
        nested!.IsEmpty.ShouldBeFalse();
        nested.TryGetValueChange<string>("Host", out var host).ShouldBeTrue();
        host.ShouldNotBeNull();
        host!.IsChanged.ShouldBeTrue();
        host.Before.Value.ShouldBe("host-before");
        host.After.Value.ShouldBe("host-after");

        // The untouched scalar still resolves through the same entry point.
        changes.TryGetValueChange<string?>("Label", out var label).ShouldBeTrue();
        label.ShouldNotBeNull();
        label!.IsChanged.ShouldBeFalse();
    }

    [Test]
    public void RebasedChangeSetKeepsItsModelType()
    {
        var baseline = Present(
            MakeSettings(
                Optional<string?>.Present("before"),
                Optional<int>.Present(1),
                Optional<Nested.Fragment?>.Missing
            )
        );
        var edited = Present(
            MakeSettings(
                Optional<string?>.Present("after"),
                Optional<int>.Present(1),
                Optional<Nested.Fragment?>.Missing
            )
        );

        ISparseChangeSet<Settings> rebased = Settings
            .ChangeSet.Between(baseline, edited)
            .RebaseOnto(baseline)
            .Rebased;

        rebased.IsChanged.ShouldBeTrue();
    }

    [Test]
    public void ChangeSetForAnotherModelDoesNotConvertAtCompileTime()
    {
        const string source = """
            public static class CompileProbe
            {
                private static void Accept(
                    global::SparseFragments.ISparseChangeSet<global::SparseFragments.Tests.Nested> changes
                ) { }

                public static void PassWrongModel(
                    global::SparseFragments.Tests.Settings.ChangeSet changes
                ) => Accept(changes);
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trustedAssemblies
            .Select(static assemblyPath =>
                (MetadataReference)MetadataReference.CreateFromFile(assemblyPath)
            )
            .Append(
                MetadataReference.CreateFromFile(
                    typeof(SparseFragmentModelAttribute).Assembly.Location
                )
            )
            .Append(MetadataReference.CreateFromFile(typeof(Settings).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "TypedChangeSetCompileProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Id)
            .ShouldContain("CS1503");
    }

    private static Optional<CompositeServerHolder.Fragment?> Composite(
        params CompositeServer[] items
    ) =>
        Optional<CompositeServerHolder.Fragment?>.Present(
            CompositeServerHolder.Fragment.From(
                new CompositeServerHolder { Items = items.ToList() }
            )
        );

    [Test]
    public void CompositeKeysExposeAddsEditsAndNestedChanges()
    {
        var before = Composite(
            new CompositeServer
            {
                TenantId = "t1",
                Id = "a",
                Name = "A",
            }
        );
        var after = Composite(
            new CompositeServer
            {
                TenantId = "t1",
                Id = "a",
                Name = "A2",
            },
            new CompositeServer
            {
                TenantId = "t2",
                Id = "a",
                Name = "B",
            }
        );
        ISparseChangeSet changes = CompositeServerHolder.ChangeSet.Between(before, after);
        changes
            .TryGetKeyedChange<
                (string TenantId, string Id),
                CompositeServer,
                CompositeServer.ChangeSet
            >("Items", out var items)
            .ShouldBeTrue();
        items.ShouldNotBeNull();

        // Membership growth changes the key order list; no surviving item is
        // reordered by relative rank.
        items!.OrderChanged.ShouldBeTrue();
        items.Added.Count.ShouldBe(1);
        items.Removed.ShouldBeEmpty();
        items.Edited.Count.ShouldBe(1);

        var edited = items.GetChange(("t1", "a"));
        edited.IsEdited.ShouldBeTrue();
        edited.IsReordered.ShouldBeFalse();
        ISparseChangeSet edit = edited.Edit;
        edit.TryGetValueChange<string>("Name", out var name).ShouldBeTrue();
        name.ShouldNotBeNull();
        name!.After.Value.ShouldBe("A2");

        // The generic summary helper only knows the contracts.
        ExternalConsumer
            .SummarizeKeyed(items)
            .ShouldBe(["(t1, a):edited:0->0", "(t2, a):added:-1->1"]);
    }

    private static Optional<KeyedServerHolder.Fragment?> Keyed(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
        );

    private static KeyedServer Server(string id, string? name = null) =>
        new() { Id = id, Name = name ?? id };

    [Test]
    public void PureReorderMarksRelativeRankOnly()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(Server("a"), Server("b"), Server("c")),
            Keyed(Server("b"), Server("a"), Server("c"))
        );
        ISparseChangeSet root = changes;
        root.TryGetKeyedChange<string, KeyedServer, KeyedServer.ChangeSet>("Items", out var items)
            .ShouldBeTrue();
        items.ShouldNotBeNull();

        items!.OrderChanged.ShouldBeTrue();
        items.Added.ShouldBeEmpty();
        items.Removed.ShouldBeEmpty();
        items.Edited.ShouldBeEmpty();
        items.BeforeOrder.ShouldBe(["a", "b", "c"]);
        items.AfterOrder.ShouldBe(["b", "a", "c"]);

        var reordered = items.GetChange("a");
        reordered.IsReordered.ShouldBeTrue();
        reordered.BeforeIndex.ShouldBe(0);
        reordered.AfterIndex.ShouldBe(1);

        // Absolute index shift without relative-rank change is not a reorder.
        var removal = KeyedServerHolder.ChangeSet.Between(
            Keyed(Server("a"), Server("b"), Server("c")),
            Keyed(Server("b"), Server("c"))
        );
        ISparseChangeSet removalRoot = removal;
        removalRoot
            .TryGetKeyedChange<string, KeyedServer, KeyedServer.ChangeSet>("Items", out var kept)
            .ShouldBeTrue();
        kept.ShouldNotBeNull();
        // Pure-removal survivors carry no semantic change at all.
        kept!.GetChange("b").IsEmpty.ShouldBeTrue();
        kept.GetChange("c").IsEmpty.ShouldBeTrue();
        kept.GetChange("missing").IsEmpty.ShouldBeTrue();
        var gone = kept.GetChange("a");
        gone.IsRemoved.ShouldBeTrue();
        gone.IsReordered.ShouldBeFalse();
        gone.BeforeIndex.ShouldBe(0);
        gone.AfterIndex.ShouldBe(-1);

        // An edited survivor keeps absolute indexes alongside a false reorder flag.
        var shifted = KeyedServerHolder.ChangeSet.Between(
            Keyed(Server("a"), Server("b"), Server("c")),
            Keyed(Server("b", "b2"), Server("c"))
        );
        ISparseChangeSet shiftedRoot = shifted;
        shiftedRoot
            .TryGetKeyedChange<string, KeyedServer, KeyedServer.ChangeSet>(
                "Items",
                out var editedSurvivor
            )
            .ShouldBeTrue();
        editedSurvivor.ShouldNotBeNull();
        var edited = editedSurvivor!.GetChange("b");
        edited.IsEdited.ShouldBeTrue();
        edited.IsReordered.ShouldBeFalse();
        edited.BeforeIndex.ShouldBe(1);
        edited.AfterIndex.ShouldBe(0);

        ExternalConsumer.SummarizeKeyed(items).ShouldBe(["b:reordered:1->0", "a:reordered:0->1"]);
    }

    [Test]
    public void NestedKeyedEditsRecurseWithoutGeneratedNames()
    {
        var before = Optional<ClusterHolder.Fragment?>.Present(
            ClusterHolder.Fragment.From(
                new ClusterHolder
                {
                    Groups = new List<ServerGroup>
                    {
                        new()
                        {
                            Name = "g1",
                            Servers = new List<KeyedServer> { Server("s1") },
                        },
                    },
                }
            )
        );
        var after = Optional<ClusterHolder.Fragment?>.Present(
            ClusterHolder.Fragment.From(
                new ClusterHolder
                {
                    Groups = new List<ServerGroup>
                    {
                        new()
                        {
                            Name = "g1",
                            Servers = new List<KeyedServer>
                            {
                                new KeyedServer
                                {
                                    Id = "s1",
                                    Name = "renamed",
                                    Count = 1,
                                },
                            },
                        },
                    },
                }
            )
        );
        ISparseChangeSet root = ClusterHolder.ChangeSet.Between(before, after);
        root.TryGetKeyedChange<string, ServerGroup, ServerGroup.ChangeSet>("Groups", out var groups)
            .ShouldBeTrue();
        groups.ShouldNotBeNull();

        var group = groups!.GetChange("g1");
        group.IsEdited.ShouldBeTrue();
        ISparseChangeSet groupEdit = group.Edit;
        groupEdit
            .TryGetKeyedChange<string, KeyedServer, KeyedServer.ChangeSet>(
                "Servers",
                out var servers
            )
            .ShouldBeTrue();
        servers.ShouldNotBeNull();
        var server = servers!.GetChange("s1");
        server.IsEdited.ShouldBeTrue();
        server.Edit.Name.After.Value.ShouldBe("renamed");
    }

    [Test]
    public void DictionariesExposeTypedChangesThroughContracts()
    {
        var before = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(
                new ScalarDictHolder
                {
                    Scores = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
                }
            )
        );
        var after = Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(
                new ScalarDictHolder
                {
                    Scores = new Dictionary<string, int> { ["b"] = 3, ["c"] = 4 },
                }
            )
        );
        ISparseChangeSet root = ScalarDictHolder.ChangeSet.Between(before, after);
        root.TryGetDictionaryChange<string, int, int>("Scores", out var scores).ShouldBeTrue();
        scores.ShouldNotBeNull();
        scores!.Added["c"].ShouldBe(4);
        scores.Removed["a"].ShouldBe(1);
        scores.Edited["b"].ShouldBe(3);
        scores.GetChange("b").IsEdited.ShouldBeTrue();
        scores.EnumerateEntries().Select(static entry => entry.Key).ShouldBe(["c", "a", "b"]);

        var structuralBefore = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new StructuralDictHolder
                {
                    Servers = new Dictionary<string, KeyedServer> { ["web"] = Server("web") },
                }
            )
        );
        var structuralAfter = Optional<StructuralDictHolder.Fragment?>.Present(
            StructuralDictHolder.Fragment.From(
                new StructuralDictHolder
                {
                    Servers = new Dictionary<string, KeyedServer>
                    {
                        ["web"] = new KeyedServer
                        {
                            Id = "web",
                            Name = "renamed",
                            Count = 1,
                        },
                    },
                }
            )
        );
        ISparseChangeSet structuralRoot = StructuralDictHolder.ChangeSet.Between(
            structuralBefore,
            structuralAfter
        );
        structuralRoot
            .TryGetDictionaryChange<string, KeyedServer, KeyedServer.ChangeSet>(
                "Servers",
                out var servers
            )
            .ShouldBeTrue();
        servers.ShouldNotBeNull();
        var entry = servers!.GetChange("web");
        entry.IsEdited.ShouldBeTrue();
        var structural =
            entry
            as ISparseStructuralDictionaryItemChange<string, KeyedServer, KeyedServer.ChangeSet>;
        structural.ShouldNotBeNull();
        structural!.Edit.Name.After.Value.ShouldBe("renamed");
    }

    private static Optional<TempOrderBook.Fragment?> Book(TempOrderBook book) =>
        Optional<TempOrderBook.Fragment?>.Present(TempOrderBook.Fragment.From(book));

    [Test]
    public void TemporaryIdentityStaysScopedToItsCollection()
    {
        var shared = Guid.NewGuid();
        var before = Book(new TempOrderBook());
        var after = Book(
            new TempOrderBook
            {
                Customer = new TempCustomer
                {
                    Orders = new List<TempOrderLine>
                    {
                        new() { Name = "nested", TemporaryId = shared },
                    },
                },
                Lines = new List<TempOrderLine>
                {
                    new() { Name = "root", TemporaryId = shared },
                },
            }
        );
        ISparseChangeSet root = TempOrderBook.ChangeSet.Between(before, after);

        root.TryGetNestedChange("Customer", out var customer).ShouldBeTrue();
        customer.ShouldNotBeNull();
        customer!
            .TryGetKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>(
                "Orders",
                out var orders
            )
            .ShouldBeTrue();
        orders.ShouldNotBeNull();
        var nestedTemp =
            orders as ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>;
        nestedTemp.ShouldNotBeNull();
        var nested = nestedTemp!.GetTemporaryChange(shared);
        nested.IsAdded.ShouldBeTrue();
        nested.TemporaryKey.ShouldBe(shared);
        nested.After.Value!.Name.ShouldBe("nested");

        root.TryGetKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>("Lines", out var lines)
            .ShouldBeTrue();
        lines.ShouldNotBeNull();
        var rootTemp =
            lines as ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>;
        rootTemp.ShouldNotBeNull();
        var atRoot = rootTemp!.GetTemporaryChange(shared);
        atRoot.IsAdded.ShouldBeTrue();
        atRoot.After.Value!.Name.ShouldBe("root");

        // Same identifier, distinct collection scopes, distinct items.
        ReferenceEquals(nested, atRoot).ShouldBeFalse();
        rootTemp.GetTemporaryChange(Guid.NewGuid()).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AssignedKeyWinsOverTemporaryIdentityThroughContracts()
    {
        var temp = Guid.NewGuid();
        var before = Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder { Lines = new List<TempOrderLine>() })
        );
        var after = Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(
                new TempOrderHolder
                {
                    Lines = new List<TempOrderLine>
                    {
                        new()
                        {
                            Id = 7,
                            Name = "saved",
                            TemporaryId = temp,
                        },
                    },
                }
            )
        );
        ISparseChangeSet root = TempOrderHolder.ChangeSet.Between(before, after);
        root.TryGetKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>("Lines", out var lines)
            .ShouldBeTrue();
        lines.ShouldNotBeNull();

        // Assigned entries resolve by permanent key with no temporary identity.
        var added = lines!.GetChange(7);
        added.IsAdded.ShouldBeTrue();
        added.TemporaryKey.ShouldBeNull();

        var temporary =
            lines as ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>;
        temporary.ShouldNotBeNull();
        temporary!.GetTemporaryChange(temp).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ChangeRecordsReuseTheTypedEnumerationProjection()
    {
        var before = Present(
            MakeSettings(
                Optional<string?>.Present("before"),
                Optional<int>.Missing,
                NestedState("host-before")
            )
        );
        var after = Present(
            MakeSettings(
                Optional<string?>.Present(null),
                Optional<int>.Present(3),
                NestedState("host-after")
            )
        );
        var typed = Settings.ChangeSet.Between(before, after);
        ISparseChangeSet root = typed;

        var records = root.EnumerateChangeRecords();
        var expected = typed.EnumerateChanges().ToList();
        records.Count.ShouldBe(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            records[index].PathText.ShouldBe(expected[index].PathText);
            ((int)records[index].Kind).ShouldBe((int)expected[index].Kind);
            records[index].Before.ShouldBe(expected[index].Before);
            records[index].After.ShouldBe(expected[index].After);
        }

        records.Select(static record => record.Kind).ShouldContain(SparseChangeKind.Changed);
        ExternalConsumer.SummarizeRecords(root).ShouldNotBeEmpty();
    }
}
