namespace SparseTypes.Tests;

public sealed class EditSessionChildTests
{
    [Test]
    public void AcceptingChildMergesDisjointParentChangesAndPreservesIdentity()
    {
        var childModel = new ObservableChild { Name = "before", Count = 1 };
        var model = new ObservableHolder { Title = "before", Child = childModel };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);

        child.Session.Observable.Name = "child edit";
        session.Observable.Title = "parent edit";

        child.TryAcceptChanges(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        ReferenceEquals(model.Child, childModel).ShouldBeTrue();
        model.Child.Name.ShouldBe("child edit");
        model.Child.Count.ShouldBe(1);
        model.Title.ShouldBe("parent edit");
        child.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().EnumerateChangedPaths().Count.ShouldBe(2);
    }

    [Test]
    public void ConflictingChildAcceptLeavesParentUnchanged()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(KeyedServerHolder.SparsePath.Items.Key("server"));

        child.Session.Observable.Name = "child edit";
        session.Observable.Items[0].Name = "parent edit";

        child.TryAcceptChanges(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        conflicts!.ShouldContain(conflict => conflict.PathText == "Items[\"server\"].Name");
        session.Observable.Items[0].Name.ShouldBe("parent edit");
        child.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void KeyedChildFollowsReorderedTargetAndMissingTargetDisconnects()
    {
        var model = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "first", Name = "one" },
                new KeyedServer { Id = "second", Name = "two" },
            ],
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(KeyedServerHolder.SparsePath.Items.Key("second"));
        child.Session.Observable.Name = "updated";

        session.Observable.Items.Move(1, 0);

        child.TryAcceptChanges(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        session.Observable.Items[0].Id.ShouldBe("second");
        session.Observable.Items[0].Name.ShouldBe("updated");

        var missing = session.OpenChildSession(KeyedServerHolder.SparsePath.Items.Key("first"));
        missing.Session.Observable.Name = "lost";
        session.Observable.Items.RemoveAt(1);

        missing.TryAcceptChanges(out conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        missing.HasChanges.ShouldBeFalse();
        session.Observable.Items.Count.ShouldBe(1);
    }

    [Test]
    public void ChildAcceptMergesKeyedInsertionAtMiddleOfCollection()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers =
                    [
                        new KeyedServer { Id = "first", Name = "first" },
                        new KeyedServer { Id = "second", Name = "second" },
                    ],
                },
            ],
        };
        var session = model.CreateEditSession();
        var group = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("group"));
        var servers = group.Session.EditCollection<KeyedServer, string>(
            ServerGroup.SparsePath.Servers
        );

        servers
            .TryInsert(1, new KeyedServer { Id = "inserted", Name = "inserted" }, out var error)
            .ShouldBeTrue();

        group.TryAcceptChanges(out var conflicts).ShouldBeTrue();

        error.ShouldBe(SparseCollectionEditError.None);
        conflicts.ShouldBeNull();
        model
            .Groups[0]
            .Servers.Select(static server => server.Id)
            .ShouldBe(["first", "inserted", "second"]);
    }

    [Test]
    public void RootChangeSetAcceptsGrandchildSessionsBeforeCapture()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        var group = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("group"));
        var server = group.Session.OpenChildSession(ServerGroup.SparsePath.Servers.Key("server"));
        server.Session.Observable.Name = "grandchild";

        session.TryCreateChangeSet(out var changes, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        changes.ShouldNotBeNull();
        model.Groups[0].Servers[0].Name.ShouldBe("grandchild");
        group.HasChanges.ShouldBeFalse();
        server.HasChanges.ShouldBeFalse();
        changes!
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldContain("Groups[\"group\"].Servers[\"server\"].Name");
    }

    [Test]
    public void RootChangeSetReturnsConflictFromPendingChild()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "child edit";
        session.Observable.Child!.Name = "parent edit";

        session.TryCreateChangeSet(out var changes, out var conflicts).ShouldBeFalse();

        changes.ShouldBeNull();
        conflicts.ShouldNotBeNull();
        conflicts!.ShouldContain(conflict => conflict.PathText == "Child.Name");
        model.Child.Name.ShouldBe("parent edit");
        child.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void StateQueriesDoNotAcceptPendingChildDrafts()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(KeyedServerHolder.SparsePath.Items.Key("server"));
        child.Session.Observable.Name = "child edit";

        var field = session.FieldState(KeyedServerHolder.SparsePath.Items.Key("server").Name);
        var collection = session.CollectionState(KeyedServerHolder.SparsePath.Items);

        field.IsChanged.ShouldBeFalse();
        collection.GetItemState("server").IsEmpty.ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        model.Items[0].Name.ShouldBe("before");
    }

    [Test]
    public void ChildHistoryIsIndependentAndClearsAfterAccept()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var parentHistory = session.EnableHistory();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();

        child.Session.Observable.Name = "child edit";

        childHistory.CanUndo.ShouldBeTrue();
        parentHistory.CanUndo.ShouldBeFalse();
        childHistory.Undo();
        child.Session.Observable.Name.ShouldBe("before");
        childHistory.CanRedo.ShouldBeTrue();
        childHistory.Redo();

        child.AcceptChanges();

        childHistory.CanUndo.ShouldBeFalse();
        childHistory.CanRedo.ShouldBeFalse();
        session.CreateChangeSet().EnumerateChangedPaths().ShouldHaveSingleItem();
    }

    [Test]
    public void DiscardRestoresChildBaselineAndDisposeDetachesIt()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);

        child.Session.Observable.Name = "discarded";
        child.DiscardChanges();

        child.HasChanges.ShouldBeFalse();
        child.Session.Observable.Name.ShouldBe("before");
        session.HasChanges.ShouldBeFalse();

        child.Session.Observable.Name = "detached";
        child.Dispose();

        session.HasChanges.ShouldBeFalse();
        Should.Throw<ObjectDisposedException>(() => child.AcceptChanges());
        model.Child.Name.ShouldBe("before");
    }

    [Test]
    public void DiscardAfterAcceptRestoresTheLatestAcceptedChildBaseline()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);

        child.Session.Observable.Name = "accepted";
        child.AcceptChanges();
        child.Session.Observable.Count = 2;

        child.DiscardChanges();

        child.Session.Observable.Name.ShouldBe("accepted");
        child.Session.Observable.Count.ShouldBe(1);
        session.Observable.Child.ShouldNotBeNull().Name.ShouldBe("accepted");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void TemporaryAndDictionaryPathsCreateChildSessions()
    {
        var temporaryId = Guid.NewGuid();
        var temporaryModel = new TempOrderHolder
        {
            Lines =
            [
                new TempOrderLine
                {
                    Id = 0,
                    TemporaryId = temporaryId,
                    Name = "before",
                },
            ],
        };
        var temporarySession = temporaryModel.CreateEditSession();
        var temporaryChild = temporarySession.OpenChildSession(
            TempOrderHolder.SparsePath.Lines.TemporaryKey(temporaryId)
        );
        temporaryChild.Session.Observable.Name = "updated";
        temporaryChild.AcceptChanges();
        temporarySession.Observable.Lines[0].Name.ShouldBe("updated");

        var dictionaryModel = new StructuralDictHolder
        {
            Servers = new()
            {
                ["server"] = new KeyedServer { Id = "server", Name = "before" },
            },
        };
        var dictionarySession = dictionaryModel.CreateEditSession();
        var dictionaryChild = dictionarySession.OpenChildSession(
            StructuralDictHolder.SparsePath.Servers.Key("server")
        );
        dictionaryChild.Session.Observable.Name = "updated";
        dictionaryChild.AcceptChanges();
        dictionarySession.Observable.Servers["server"].Name.ShouldBe("updated");
    }

    [Test]
    public void DescriptorSetResolvesNestedMemberKeyAndTemporaryKeyValues()
    {
        var temporaryId = Guid.NewGuid();
        var model = new TempOrderHolder
        {
            Lines = [new TempOrderLine { Id = 0, TemporaryId = temporaryId }],
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.ResolveModel(TempOrderHolder.SparsePath.Lines.TemporaryKey(temporaryId))
            .ShouldBeSameAs(model.Lines[0]);
        session
            .Descriptors.ResolveModel(TempOrderHolder.SparsePath.Lines.TemporaryKey(Guid.NewGuid()))
            .ShouldBeNull();
    }
}
