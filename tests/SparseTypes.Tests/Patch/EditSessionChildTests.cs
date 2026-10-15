namespace SparseTypes.Tests;

public sealed class EditSessionChildTests
{
    [Test]
    public void PreviewLeavesChildPendingAndExplicitAcceptMergesIt()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "child edit";

        session.PreviewChangeSet().IsEmpty.ShouldBeTrue();
        session.HasOwnChanges.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        model.Child.Name.ShouldBe("before");

        // The child-aware preview includes the descendant without accepting it.
        session.TryCreateChangeSet(out var preview, out var previewConflicts).ShouldBeTrue();

        previewConflicts.ShouldBeNull();
        preview.ShouldNotBeNull();
        preview!
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldContain("Child.Name");
        child.HasChanges.ShouldBeTrue();
        model.Child.Name.ShouldBe("before");

        session.TryAcceptChildSessions(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        preview!
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldContain("Child.Name");
        child.HasChanges.ShouldBeFalse();
        session.HasOwnChanges.ShouldBeTrue();
    }

    [Test]
    public void ChildStatusUsesTypedPathAndIncludesDescendantChanges()
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
        var path = ClusterHolder.SparsePath.Groups.Key("group");

        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.NotOpen);
        var group = session.OpenChildSession(path);
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.OpenUnchanged);
        var server = group.Session.OpenChildSession(ServerGroup.SparsePath.Servers.Key("server"));
        server.Session.Observable.Name = "updated";

        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.OpenChanged);
        session.HasOwnChanges.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();

        group.Dispose();
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.NotOpen);
    }

    [Test]
    public void ChildBatchPreflightsEveryMergeBeforeAcceptingAnyChild()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "first",
                    Servers = [new KeyedServer { Id = "server", Name = "first before" }],
                },
                new ServerGroup
                {
                    Name = "second",
                    Servers = [new KeyedServer { Id = "server", Name = "second before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        var first = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("first"));
        var second = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("second"));
        first.Session.Observable.Servers[0].Name = "first child";
        second.Session.Observable.Servers[0].Name = "second child";
        session.Observable.Groups[1].Servers[0].Name = "parent edit";

        session.TryAcceptChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        first.HasChanges.ShouldBeTrue();
        second.HasChanges.ShouldBeTrue();
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("first before");
        session.Observable.Groups[1].Servers[0].Name.ShouldBe("parent edit");
    }

    [Test]
    public void ChildBatchRejectsOverlappingChildScopesBeforeMutation()
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
        var path = ClusterHolder.SparsePath.Groups.Key("group");
        var first = session.OpenChildSession(path);
        var duplicate = session.OpenChildSession(path);
        first.Session.Observable.Servers[0].Name = "first draft";
        duplicate.Session.Observable.Servers[0].Name = "duplicate draft";

        session.TryAcceptChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        first.HasChanges.ShouldBeTrue();
        duplicate.HasChanges.ShouldBeTrue();
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("before");
    }

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
    public void KeyedChildFollowsReorderedTargetAndMissingTargetPreservesDraft()
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
        missing.IsDetached.ShouldBeFalse();
        missing.IsTargetMissing.ShouldBeTrue();
        missing.HasChanges.ShouldBeTrue();
        missing.Session.Observable.Name.ShouldBe("lost");
        session.Observable.Items.Count.ShouldBe(1);

        missing.DiscardChanges();
        missing.HasChanges.ShouldBeFalse();
        missing.Dispose();
        session.HasPendingChildSessions.ShouldBeFalse();
    }

    [Test]
    public void ChildPreflightPreservesDraftWhenKeyedTargetIsRemoved()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(KeyedServerHolder.SparsePath.Items.Key("server"));
        child.Session.Observable.Name = "child edit";
        session.Observable.Items.Clear();

        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        session
            .GetChildSessionStatus(KeyedServerHolder.SparsePath.Items.Key("server"))
            .ShouldBe(SparseChildSessionStatus.TargetMissing);
        child.IsDetached.ShouldBeFalse();
        child.IsTargetMissing.ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("child edit");

        // Repeated preflight is stable and still preserves the draft.
        session.TryPreflightChildSessions(out conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        child.HasChanges.ShouldBeTrue();

        // Re-adding the target recovers the draft without edits being lost.
        session.Observable.Items.AddModel(new KeyedServer { Id = "server", Name = "before" });
        child.IsTargetMissing.ShouldBeFalse();
        child.TryAcceptChanges(out conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        session.Observable.Items[0].Name.ShouldBe("child edit");
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
    public void RootChangeSetPreviewIncludesGrandchildSessionsWithoutAccepting()
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

        session.TryCreateChangeSet(out var preview, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        preview.ShouldNotBeNull();
        model.Groups[0].Servers[0].Name.ShouldBe("before");
        group.HasChanges.ShouldBeTrue();
        server.HasChanges.ShouldBeTrue();
        preview!
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldContain("Groups[\"group\"].Servers[\"server\"].Name");

        session.TryAcceptChildSessions(out conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Groups[0].Servers[0].Name.ShouldBe("grandchild");
        group.HasChanges.ShouldBeFalse();
        server.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void GrandchildConflictPreflightPreservesEverySessionAndHistory()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "group",
                    Servers = [new KeyedServer { Id = "server", Name = "server before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        var parentHistory = session.EnableHistory();
        var group = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("group"));
        var groupHistory = group.Session.EnableHistory();
        var server = group.Session.OpenChildSession(ServerGroup.SparsePath.Servers.Key("server"));
        var serverHistory = server.Session.EnableHistory();

        group.Session.Observable.Name = "group draft";
        server.Session.Observable.Name = "server draft";
        session.Observable.Groups[0].Servers[0].Name = "parent draft";

        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts!
            .Select(static conflict => conflict.PathText)
            .ShouldContain("Groups[\"group\"].Servers[\"server\"].Name");
        session.TryCreateChangeSet(out var changes, out var prepareConflicts).ShouldBeFalse();

        changes.ShouldBeNull();
        prepareConflicts.ShouldNotBeNull();
        session.Observable.Groups[0].Name.ShouldBe("group");
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("parent draft");
        group.Session.Observable.Name.ShouldBe("group draft");
        group.Session.Observable.Servers[0].Name.ShouldBe("server before");
        server.Session.Observable.Name.ShouldBe("server draft");
        parentHistory.CanUndo.ShouldBeTrue();
        groupHistory.CanUndo.ShouldBeTrue();
        serverHistory.CanUndo.ShouldBeTrue();

        serverHistory.Undo();
        server.Session.Observable.Name.ShouldBe("server before");
        serverHistory.CanRedo.ShouldBeTrue();
        serverHistory.Redo();
        server.Session.Observable.Name.ShouldBe("server draft");
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

    [Test]
    public void RepeatedPreflightLeavesRootChildAndHistoryUnchanged()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "draft";

        session.TryPreflightChildSessions(out var first).ShouldBeTrue();
        first.ShouldBeNull();
        session.TryPreflightChildSessions(out var second).ShouldBeTrue();

        second.ShouldBeNull();
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("draft");
        model.Child.Name.ShouldBe("before");
        session.HasPendingChildSessions.ShouldBeTrue();
        history.CanUndo.ShouldBeFalse();
        childHistory.CanUndo.ShouldBeTrue();

        session.Observable.Child!.Name = "parent edit";
        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        session.TryPreflightChildSessions(out var repeat).ShouldBeFalse();

        repeat.ShouldNotBeNull();
        session.Observable.Child.Name.ShouldBe("parent edit");
        child.Session.Observable.Name.ShouldBe("draft");
        child.HasChanges.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();
        childHistory.Undo();
        child.Session.Observable.Name.ShouldBe("before");
        childHistory.CanRedo.ShouldBeTrue();
    }

    [Test]
    public void ReplacedDirectChildTargetReportsTypedConflictWithoutLosingDraft()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "draft";
        var replacement = new ObservableChild { Name = "replacement", Count = 1 };
        session
            .Descriptors.TryGet(nameof(ObservableHolder.Child), out var childField)
            .ShouldBeTrue();
        childField.TrySetValue(replacement).ShouldBeTrue();

        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();

        // The target exists with different values, so this is a typed conflict, not a disappearance.
        conflicts.ShouldNotBeNull();
        child.IsDetached.ShouldBeFalse();
        child.IsTargetMissing.ShouldBeFalse();
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("draft");
        session
            .GetChildSessionStatus(ObservableHolder.SparsePath.Child)
            .ShouldBe(SparseChildSessionStatus.OpenChanged);

        child.DiscardChanges();

        child.HasChanges.ShouldBeFalse();
        model.Child.Name.ShouldBe("replacement");
        child.Session.Observable.Name.ShouldBe("before");
        session.TryPreflightChildSessions(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
    }

    [Test]
    public void GrandchildMissingTargetPreservesEveryDraftAndHistory()
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
        var history = session.EnableHistory();
        var groupPath = ClusterHolder.SparsePath.Groups.Key("group");
        var group = session.OpenChildSession(groupPath);
        var groupHistory = group.Session.EnableHistory();
        var serverPath = ServerGroup.SparsePath.Servers.Key("server");
        var server = group.Session.OpenChildSession(serverPath);
        var serverHistory = server.Session.EnableHistory();

        server.Session.Observable.Name = "server draft";
        session.Observable.Groups[0].Servers.Clear();

        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        session.GetChildSessionStatus(groupPath).ShouldBe(SparseChildSessionStatus.OpenChanged);
        // The grandchild still resolves against its immediate parent session (the group draft fork),
        // which retains the deleted row; only the root projection observes the disappearance.
        group
            .Session.GetChildSessionStatus(serverPath)
            .ShouldBe(SparseChildSessionStatus.OpenChanged);
        // Missing-ness is scoped to the draft's immediate parent session: the group draft fork
        // still contains the row, so only the root projection reports the disappearance.
        server.IsTargetMissing.ShouldBeFalse();
        server.IsDetached.ShouldBeFalse();
        server.HasChanges.ShouldBeTrue();
        server.Session.Observable.Name.ShouldBe("server draft");
        // The child session keeps its fork snapshot: the deleted row is still visible there
        // with its pre-edit values, while the root projection observes the disappearance.
        group.Session.Observable.Servers.ShouldHaveSingleItem();
        group.Session.Observable.Servers[0].Name.ShouldBe("before");
        history.CanUndo.ShouldBeTrue();
        // Descendant edits record in their own session only.
        groupHistory.CanUndo.ShouldBeFalse();
        serverHistory.CanUndo.ShouldBeTrue();

        session
            .Observable.Groups[0]
            .Servers.AddModel(new KeyedServer { Id = "server", Name = "before" });
        session.ResolveChildModel(groupPath).ShouldNotBeNull();
        session.TryAcceptChildSessions(out conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Groups[0].Name.ShouldBe("group");
        model.Groups[0].Servers[0].Name.ShouldBe("server draft");
        server.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void DictionaryChildMissingTargetPreservesDraft()
    {
        var model = new StructuralDictHolder
        {
            Servers = new()
            {
                ["server"] = new KeyedServer { Id = "server", Name = "before" },
            },
        };
        var session = model.CreateEditSession();
        var path = StructuralDictHolder.SparsePath.Servers.Key("server");
        var child = session.OpenChildSession(path);
        child.Session.Observable.Name = "draft";
        session.Observable.Servers.Remove("server");

        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.TargetMissing);
        child.IsDetached.ShouldBeFalse();
        child.IsTargetMissing.ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("draft");

        child.DiscardChanges();

        child.HasChanges.ShouldBeFalse();
        session.TryPreflightChildSessions(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
    }

    [Test]
    public void TemporaryKeyChildMissingTargetPreservesDraft()
    {
        var temporaryId = Guid.NewGuid();
        var model = new TempOrderHolder
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
        var session = model.CreateEditSession();
        var path = TempOrderHolder.SparsePath.Lines.TemporaryKey(temporaryId);
        var child = session.OpenChildSession(path);
        child.Session.Observable.Name = "draft";
        session.Observable.Lines.RemoveAt(0);

        session.TryPreflightChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.TargetMissing);
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("draft");

        // A replacement row under the same temporary identity recovers the draft.
        session.Observable.Lines.AddModel(
            new TempOrderLine
            {
                Id = 0,
                TemporaryId = temporaryId,
                Name = "before",
            }
        );
        child.TryAcceptChanges(out conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        session.Observable.Lines[0].Name.ShouldBe("draft");
    }

    [Test]
    public void ChildSessionStatusTracksMissingTargetLifecycle()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        var path = KeyedServerHolder.SparsePath.Items.Key("server");

        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.NotOpen);
        var child = session.OpenChildSession(path);
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.OpenUnchanged);

        child.Session.Observable.Name = "draft";
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.OpenChanged);

        session.Observable.Items.Clear();
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.TargetMissing);

        session.Observable.Items.AddModel(new KeyedServer { Id = "server", Name = "before" });
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.OpenChanged);

        child.TryAcceptChanges(out _).ShouldBeTrue();
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.OpenUnchanged);

        child.Dispose();
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.NotOpen);
    }

    [Test]
    public void DuplicateChildPathsFailAcceptanceWithoutMutation()
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
        var history = session.EnableHistory();
        var path = ClusterHolder.SparsePath.Groups.Key("group");
        var first = session.OpenChildSession(path);
        var second = session.OpenChildSession(path);
        var firstHistory = first.Session.EnableHistory();
        var secondHistory = second.Session.EnableHistory();
        first.Session.Observable.Servers[0].Name = "first draft";
        second.Session.Observable.Servers[0].Name = "second draft";

        session.TryAcceptChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldBeNull();
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("before");
        model.Groups[0].Servers[0].Name.ShouldBe("before");
        first.Session.Observable.Servers[0].Name.ShouldBe("first draft");
        second.Session.Observable.Servers[0].Name.ShouldBe("second draft");
        first.HasChanges.ShouldBeTrue();
        second.HasChanges.ShouldBeTrue();
        history.CanUndo.ShouldBeFalse();
        firstHistory.CanUndo.ShouldBeTrue();
        secondHistory.CanUndo.ShouldBeTrue();

        session.TryAcceptChildSessions(out conflicts).ShouldBeFalse();
        conflicts.ShouldBeNull();
    }

    [Test]
    public void ConflictingSiblingAcceptPreservesDraftsHistoriesAndRevision()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "first",
                    Servers = [new KeyedServer { Id = "server", Name = "first before" }],
                },
                new ServerGroup
                {
                    Name = "second",
                    Servers = [new KeyedServer { Id = "server", Name = "second before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var first = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("first"));
        var second = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("second"));
        var firstHistory = first.Session.EnableHistory();
        var secondHistory = second.Session.EnableHistory();
        first.Session.Observable.Servers[0].Name = "first child";
        second.Session.Observable.Servers[0].Name = "second child";
        session.Observable.Groups[1].Servers[0].Name = "parent edit";

        session.TryAcceptChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        // Neither child merged: the parent edit stands alone.
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("first before");
        session.Observable.Groups[1].Servers[0].Name.ShouldBe("parent edit");
        model.Groups[0].Servers[0].Name.ShouldBe("first before");
        model.Groups[1].Servers[0].Name.ShouldBe("parent edit");
        first.Session.Observable.Servers[0].Name.ShouldBe("first child");
        second.Session.Observable.Servers[0].Name.ShouldBe("second child");
        first.HasChanges.ShouldBeTrue();
        second.HasChanges.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();
        firstHistory.CanUndo.ShouldBeTrue();
        secondHistory.CanUndo.ShouldBeTrue();

        history.Undo();
        session.Observable.Groups[1].Servers[0].Name.ShouldBe("second before");
        session.TryAcceptChildSessions(out conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Groups[0].Servers[0].Name.ShouldBe("first child");
        model.Groups[1].Servers[0].Name.ShouldBe("second child");
    }

    [Test]
    public void NotificationFailureDuringCommitRollsBackWithoutAcknowledging()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "first",
                    Servers = [new KeyedServer { Id = "server", Name = "first before" }],
                },
                new ServerGroup
                {
                    Name = "second",
                    Servers = [new KeyedServer { Id = "server", Name = "second before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var first = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("first"));
        var second = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("second"));
        var firstHistory = first.Session.EnableHistory();
        var secondHistory = second.Session.EnableHistory();
        first.Session.Observable.Servers[0].Name = "first child";
        second.Session.Observable.Servers[0].Name = "second child";

        var armed = true;
        session.PropertyChanged += (_, _) =>
        {
            if (armed)
            {
                armed = false;
                throw new InvalidOperationException("Notification handler failed.");
            }
        };

        var failure = Should.Throw<InvalidOperationException>(() =>
        {
            session.TryAcceptChildSessions(out _);
        });

        failure.Message.ShouldBe("Notification handler failed.");
        // The single commit rolled back: no child value reached the parent.
        session.Observable.Groups[0].Servers[0].Name.ShouldBe("first before");
        session.Observable.Groups[1].Servers[0].Name.ShouldBe("second before");
        model.Groups[0].Servers[0].Name.ShouldBe("first before");
        model.Groups[1].Servers[0].Name.ShouldBe("second before");
        // No child was acknowledged.
        first.HasChanges.ShouldBeTrue();
        second.HasChanges.ShouldBeTrue();
        first.Session.Observable.Servers[0].Name.ShouldBe("first child");
        second.Session.Observable.Servers[0].Name.ShouldBe("second child");
        firstHistory.CanUndo.ShouldBeTrue();
        secondHistory.CanUndo.ShouldBeTrue();
        // The rolled-back attempt remains visible in the parent history; only the model
        // state is restored, and no child baseline or history was acknowledged.
        history.CanUndo.ShouldBeTrue();

        session.TryAcceptChildSessions(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Groups[0].Servers[0].Name.ShouldBe("first child");
        model.Groups[1].Servers[0].Name.ShouldBe("second child");
    }

    [Test]
    public void ConfirmWithoutCommitThrowsOnMissingTargetAndPreservesDraft()
    {
        var model = new ClusterHolder
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "first",
                    Servers = [new KeyedServer { Id = "server", Name = "first before" }],
                },
                new ServerGroup
                {
                    Name = "second",
                    Servers = [new KeyedServer { Id = "server", Name = "second before" }],
                },
            ],
        };
        var session = model.CreateEditSession();
        var first = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("first"));
        var second = session.OpenChildSession(ClusterHolder.SparsePath.Groups.Key("second"));
        var firstHistory = first.Session.EnableHistory();
        var secondHistory = second.Session.EnableHistory();
        first.Session.Observable.Servers[0].Name = "first child";
        second.Session.Observable.Servers[0].Name = "second child";

        // Confirming without a commit throws on the missing target without closing any draft.
        session.Observable.Groups.RemoveAt(1);
        second.IsTargetMissing.ShouldBeTrue();
        var failure = Should.Throw<InvalidOperationException>(() =>
        {
            session.ConfirmChildSessions();
        });

        failure.Message.ShouldBe("The child edit session target is no longer available.");
        // Direct confirmation is coordinator machinery: without a preceding commit it
        // synchronizes drafts from live state, so only the missing-target behavior is asserted here.
        first.HasChanges.ShouldBeFalse();
        firstHistory.CanUndo.ShouldBeFalse();
        second.HasChanges.ShouldBeTrue();
        second.IsTargetMissing.ShouldBeTrue();
        second.IsDetached.ShouldBeFalse();
        second.Session.Observable.Servers[0].Name.ShouldBe("second child");
        secondHistory.CanUndo.ShouldBeTrue();

        session.Observable.Groups.AddModel(
            new ServerGroup
            {
                Name = "second",
                Servers = [new KeyedServer { Id = "server", Name = "second before" }],
            }
        );
        session.TryAcceptChildSessions(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Groups[1].Servers[0].Name.ShouldBe("second child");
        second.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void TryCreateChangeSetComposesOwnAndDescendantEdits()
    {
        var model = new ObservableHolder
        {
            Title = "before",
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "child edit";
        session.Observable.Title = "parent edit";

        session.TryCreateChangeSet(out var preview, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        preview.ShouldNotBeNull();
        preview!
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldBe(["Title", "Child.Name"], ignoreOrder: true);
        // Neither side was accepted: the child edit stays in its draft fork.
        child.HasChanges.ShouldBeTrue();
        session.HasOwnChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("child edit");
        session.Observable.Title.ShouldBe("parent edit");
        session.Observable.Child!.Name.ShouldBe("before");
    }

    [Test]
    public void CreateChangeSetPreviewThrowsWithoutMutatingOnConflict()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "child edit";
        session.Observable.Child!.Name = "parent edit";

        var failure = Should.Throw<InvalidOperationException>(() =>
        {
            session.CreateChangeSet();
        });

        failure.Message.ShouldContain("conflicts");
        session.Observable.Child.Name.ShouldBe("parent edit");
        child.Session.Observable.Name.ShouldBe("child edit");
        child.HasChanges.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();
    }
}
