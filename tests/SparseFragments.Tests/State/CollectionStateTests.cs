using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Nullable whole-presence collection fixtures for state tests (issue #250).</summary>
[SparseFragmentModel]
public partial class StateNullableHolder
{
    public List<KeyedServer>? MaybeItems { get; set; }

    public Dictionary<string, int>? MaybeScores { get; set; }
}

/// <summary>Path-first keyed and dictionary collection state (issue #250).</summary>
public sealed class CollectionStateTests
{
    private static KeyedServerHolder Holder(params (string Id, string Name)[] rows) =>
        new()
        {
            Items = rows
                .Select(row => new KeyedServer { Id = row.Id, Name = row.Name })
                .ToList(),
        };

    [Test]
    public void CleanCollectionReportsEmpty()
    {
        var session = Holder(("a", "a")).CreateEditSession();

        var view = session.CollectionState(KeyedServerHolder.SparsePath.Items);
        view.Path.ShouldBe(SparsePath.Root<KeyedServerHolder>().Member("Items"));
        view.IsEmpty.ShouldBeTrue();
        view.IsChanged.ShouldBeFalse();
        view.OrderChanged.ShouldBeFalse();

        var row = view.GetItemState("a");
        row.IsEmpty.ShouldBeTrue();
        row.IsChanged.ShouldBeFalse();
        row.Before.IsPresent.ShouldBeFalse();
        row.After.IsPresent.ShouldBeFalse();

        view.GetTemporaryState(Guid.NewGuid()).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AddedRemovedEditedRows()
    {
        var session = Holder(("b", "b"), ("c", "c")).CreateEditSession();
        session.Observable.Items.First(item => item.Id == "b").Name = "b2";
        session.Observable.Items.RemoveAt(1);
        session.Observable.Items.AddModel(new KeyedServer { Id = "d", Name = "d" });

        var view = session.CollectionState(KeyedServerHolder.SparsePath.Items);
        view.IsEmpty.ShouldBeFalse();
        view.IsChanged.ShouldBeTrue();

        var added = view.GetItemState("d");
        added.IsEmpty.ShouldBeFalse();
        added.IsAdded.ShouldBeTrue();
        added.IsRemoved.ShouldBeFalse();
        added.IsEdited.ShouldBeFalse();
        added.IsReordered.ShouldBeFalse();
        added.Before.IsPresent.ShouldBeFalse();
        added.After.Value.ShouldBeOfType<KeyedServer>().Id.ShouldBe("d");
        added.BeforeIndex.ShouldBe(-1);
        added.AfterIndex.ShouldBe(1);

        var removed = view.GetItemState("c");
        removed.IsRemoved.ShouldBeTrue();
        removed.Before.Value.ShouldBeOfType<KeyedServer>().Id.ShouldBe("c");
        removed.After.IsPresent.ShouldBeFalse();
        removed.BeforeIndex.ShouldBe(1);
        removed.AfterIndex.ShouldBe(-1);

        var edited = view.GetItemState("b");
        edited.IsEdited.ShouldBeTrue();
        edited.IsAdded.ShouldBeFalse();
        edited.IsRemoved.ShouldBeFalse();

        view.GetItemState("absent").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void EditOnlyKeepsOrderStable()
    {
        var session = Holder(("a", "a"), ("b", "b")).CreateEditSession();
        session.Observable.Items[1].Name = "b2";

        var view = session.CollectionState(KeyedServerHolder.SparsePath.Items);
        view.OrderChanged.ShouldBeFalse();
        view.GetItemState("b").IsEdited.ShouldBeTrue();
        view.GetItemState("b").IsReordered.ShouldBeFalse();
        view.GetItemState("a").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void PureReorderMarksSurvivorRank()
    {
        var session = Holder(("a", "a"), ("b", "b"), ("c", "c"), ("d", "d"))
            .CreateEditSession();
        var items = session.Observable.Items;
        (items[0], items[1]) = (items[1], items[0]);

        var view = session.CollectionState(KeyedServerHolder.SparsePath.Items);
        view.OrderChanged.ShouldBeTrue();
        view.GetItemState("a").IsReordered.ShouldBeTrue();
        view.GetItemState("b").IsReordered.ShouldBeTrue();

        // Untouched survivors keep their relative rank: no reorder flag.
        view.GetItemState("c").IsReordered.ShouldBeFalse();
        view.GetItemState("c").IsEmpty.ShouldBeTrue();
        view.GetItemState("d").IsReordered.ShouldBeFalse();
    }

    [Test]
    public void KeyChangeReportsRemovePlusAdd()
    {
        var session = Holder(("a", "a"), ("b", "b")).CreateEditSession();
        session.Observable.Items.First(item => item.Id == "a").Id = "z";

        // Keys are identity: renaming is a removal plus an addition.
        var view = session.CollectionState(KeyedServerHolder.SparsePath.Items);
        var removed = view.GetItemState("a");
        removed.IsRemoved.ShouldBeTrue();
        var added = view.GetItemState("z");
        added.IsAdded.ShouldBeTrue();
        added.After.Value.ShouldBeOfType<KeyedServer>().Name.ShouldBe("a");
    }

    [Test]
    public void MultipleUnassignedItemsHaveDistinctTempStates()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var session = new TempOrderHolder().CreateEditSession();
        session.Observable.Lines.AddModel(new TempOrderLine { Name = "one", TemporaryId = first });
        session.Observable.Lines.AddModel(new TempOrderLine { Name = "two", TemporaryId = second });

        var view = session.CollectionState(TempOrderHolder.SparsePath.Lines);
        view.IsChanged.ShouldBeTrue();

        var one = view.GetTemporaryState(first);
        one.IsAdded.ShouldBeTrue();
        one.After.Value.ShouldBeOfType<TempOrderLine>().Name.ShouldBe("one");

        var two = view.GetTemporaryState(second);
        two.IsAdded.ShouldBeTrue();
        two.After.Value.ShouldBeOfType<TempOrderLine>().Name.ShouldBe("two");

        // The unassigned sentinel is never a stable lookup.
        view.GetItemState(0).IsEmpty.ShouldBeTrue();
        view.GetTemporaryState(Guid.NewGuid()).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AssignedKeyWinsOverTemporaryIdentity()
    {
        var temp = Guid.NewGuid();
        var session = new TempOrderHolder
        {
            Lines = [new TempOrderLine { Id = 7, Name = "kept", TemporaryId = temp }],
        }.CreateEditSession();
        session.Observable.Lines[0].Name = "edited";

        var view = session.CollectionState(TempOrderHolder.SparsePath.Lines);

        // Assigned entries resolve by permanent key even while retaining a temp value.
        var byKey = view.GetItemState(7);
        byKey.IsEdited.ShouldBeTrue();

        // ...and never through temporary lookup.
        view.GetTemporaryState(temp).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NullToPopulatedCollectionReportsWholeMemberChangeWithTypedRows()
    {
        var model = new StateNullableHolder();
        var session = model.CreateEditSession();
        model.MaybeItems =
        [
            new KeyedServer { Id = "a", Name = "a" },
        ];
        model.MaybeScores = new Dictionary<string, int> { ["k"] = 1 };

        // Null-to-populated reads as one member-level Changed transition with
        // a present-null before; the typed transition still carries row states.
        var view = session.CollectionState(StateNullableHolder.SparsePath.MaybeItems);
        view.IsEmpty.ShouldBeFalse();
        view.IsChanged.ShouldBeTrue();

        var row = view.GetItemState("a");
        row.IsAdded.ShouldBeTrue();
        row.Before.IsPresent.ShouldBeFalse();
        row.After.Value.ShouldBeOfType<KeyedServer>().Id.ShouldBe("a");
        row.BeforeIndex.ShouldBe(-1);
        row.AfterIndex.ShouldBe(0);

        var fields = session.FieldState(StateNullableHolder.SparsePath.MaybeItems);
        fields.IsChanged.ShouldBeTrue();
        fields.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        fields.Before.IsPresent.ShouldBeTrue();
        fields.Before.Value.ShouldBeNull();
        fields.After.IsPresent.ShouldBeTrue();
        fields.After.Value.ShouldBeOfType<List<KeyedServer>>().Count.ShouldBe(1);
        fields.HasDescendantChange.ShouldBeFalse();

        var scores = session.CollectionState(StateNullableHolder.SparsePath.MaybeScores);
        var entry = scores.GetItemState("k");
        entry.IsAdded.ShouldBeTrue();
        entry.After.Value.ShouldBe(1);
    }

    [Test]
    public void MissingRootReportsAncestorChangeForEveryPath()
    {
        var present = Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(Holder(("a", "a")))
        );
        var changes = KeyedServerHolder.ChangeSet.Between(
            Optional<KeyedServerHolder.Fragment?>.Missing,
            present
        );

        // A whole-root transition replaces member entries: every member path
        // reports an ancestor change with missing endpoints.
        var root = changes.FieldState(SparsePath.Root<KeyedServerHolder>());
        root.IsChanged.ShouldBeTrue();
        root.Kind.ShouldBe(SparseFieldChangeKind.Added);

        var member = changes.FieldState(KeyedServerHolder.SparsePath.Items);
        member.IsChanged.ShouldBeFalse();
        member.HasAncestorChange.ShouldBeTrue();
        member.HasDescendantChange.ShouldBeFalse();
        member.Before.IsPresent.ShouldBeFalse();
        member.After.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void DictionaryEntries()
    {
        var session = new ScalarDictHolder
        {
            Scores = new Dictionary<string, int> { ["keep"] = 1, ["drop"] = 2 },
        }.CreateEditSession();
        session.Observable.Scores["keep"] = 10;
        session.Observable.Scores.Remove("drop");
        session.Observable.Scores["new"] = 3;

        var view = session.CollectionState(ScalarDictHolder.SparsePath.Scores);
        view.IsChanged.ShouldBeTrue();
        view.OrderChanged.ShouldBeFalse();

        var edited = view.GetItemState("keep");
        edited.IsEdited.ShouldBeTrue();
        edited.Before.Value.ShouldBe(1);
        edited.After.Value.ShouldBe(10);

        var removed = view.GetItemState("drop");
        removed.IsRemoved.ShouldBeTrue();

        var added = view.GetItemState("new");
        added.IsAdded.ShouldBeTrue();
        added.After.Value.ShouldBe(3);

        view.GetItemState("absent").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void StructuralDictionaryEntryEdit()
    {
        var session = new StructuralDictHolder
        {
            Servers = new Dictionary<string, KeyedServer>
            {
                ["s"] = new KeyedServer { Id = "s", Name = "before" },
            },
        }.CreateEditSession();
        session.Observable.Servers["s"].Name = "after";

        var view = session.CollectionState(StructuralDictHolder.SparsePath.Servers);
        var entry = view.GetItemState("s");
        entry.IsEdited.ShouldBeTrue();
        entry.IsAdded.ShouldBeFalse();
        entry.IsRemoved.ShouldBeFalse();

        // The edited leaf stays queryable through field state.
        var leaf = session.FieldState(
            StructuralDictHolder.SparsePath.Servers.Key("s").Name
        );
        leaf.IsChanged.ShouldBeTrue();
        leaf.After.Value.ShouldBe("after");
    }

    [Test]
    public void NestedKeyedRowsViaChangeSetNavigation()
    {
        var session = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "g",
                    Servers = [new KeyedServer { Id = "s", Name = "before" }],
                },
            ],
        }.CreateEditSession();
        session.Observable.Groups[0].Servers[0].Name = "after";

        // Session collection state is root-only; nested rows resolve through
        // the typed change-set navigation with full type inference.
        var changes = session.CreateChangeSet();
        var nested = changes.Groups.GetChange("g").Edit.Servers.KeyedState(
            ClusterHolder.SparsePath.Groups.Key("g").Servers
        );

        nested.IsChanged.ShouldBeTrue();
        var row = nested.GetItemState("s");
        row.IsEdited.ShouldBeTrue();
        row.IsReordered.ShouldBeFalse();

        var leaf = session.FieldState(
            ClusterHolder.SparsePath.Groups.Key("g").Servers.Key("s").Name
        );
        leaf.IsChanged.ShouldBeTrue();
        leaf.After.Value.ShouldBe("after");
    }

    [Test]
    public void NonCollectionPathsThrowDeterministically()
    {
        var scalar = new Settings().CreateEditSession();
        Should.Throw<ArgumentException>(() =>
            scalar.CollectionState(Settings.SparsePath.Label)
        );
        Should.Throw<ArgumentException>(() =>
            scalar.CollectionState(Settings.SparsePath.Nested)
        );
        Should.Throw<ArgumentException>(() =>
            scalar.CollectionState(SparsePath.Root<Settings>().Member("Missing"))
        );
        Should.Throw<ArgumentException>(() =>
            scalar.CollectionState(SparsePath.Root<Nested>().Member("Host"))
        );

        var keyed = Holder(("a", "a")).CreateEditSession();
        Should.Throw<ArgumentException>(() =>
            keyed.CollectionState(KeyedServerHolder.SparsePath.Items.Key("a"))
        );
        Should.Throw<ArgumentException>(() =>
            keyed.CollectionState(SparsePath.Root<KeyedServerHolder>().Member("Missing"))
        );

        // A model without keyed or dictionary members always throws.
        Should.Throw<ArgumentException>(() =>
            scalar.CollectionState(Settings.SparsePath.Label)
        );
    }
}
