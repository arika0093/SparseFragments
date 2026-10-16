namespace SparseTypes.Tests;

public sealed class EditSessionChildAcceptanceTests
{
    [Test]
    public void AcceptingConvergedChildAcknowledgesItsBaselineHistoryAndPendingState()
    {
        var childModel = new ObservableChild { Name = "before" };
        var model = new ObservableHolder { Child = childModel };
        var session = model.CreateEditSession();
        var parentHistory = session.EnableHistory();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "same";
        session.Observable.Child!.Name = "same";
        var parentTransitions = 0;
        var parentNotifications = 0;
        session.TransitionObserved += _ => parentTransitions++;
        session.PropertyChanged += (_, _) => parentNotifications++;
        var transitionsBeforeAccept = parentTransitions;
        var notificationsBeforeAccept = parentNotifications;

        session.HasPendingChildSessions.ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();

        var accepted = session.TryAcceptChildSessions(out var conflicts);
        accepted.ShouldBeTrue(
            $"Unexpected conflicts: {string.Join(", ", conflicts?.Select(static conflict => conflict.PathText) ?? [])}"
        );

        conflicts.ShouldBeNull();
        ReferenceEquals(model.Child, childModel).ShouldBeTrue();
        model.Child.Name.ShouldBe("same");
        child.Session.Observable.Name.ShouldBe("same");
        child.Session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        child.HasChanges.ShouldBeFalse();
        childHistory.CanUndo.ShouldBeFalse();
        childHistory.CanRedo.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeFalse();
        session.HasOwnChanges.ShouldBeTrue();
        parentHistory.CanUndo.ShouldBeTrue();
        parentTransitions.ShouldBe(transitionsBeforeAccept);
        parentNotifications.ShouldBe(notificationsBeforeAccept);
    }

    [Test]
    public void AcceptingConvergedSiblingSessionsAcknowledgesEveryChild()
    {
        var firstModel = new ObservableChild { Name = "before" };
        var secondModel = new ObservableChild { Name = "before" };
        var model = new ObservableHolder { Child = firstModel, MaybeChild = secondModel };
        var session = model.CreateEditSession();
        var parentHistory = session.EnableHistory();
        var first = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var second = session.OpenChildSession(ObservableHolder.SparsePath.MaybeChild);
        var firstHistory = first.Session.EnableHistory();
        var secondHistory = second.Session.EnableHistory();
        first.Session.Observable.Name = "first same";
        second.Session.Observable.Name = "second same";
        session.Observable.Child!.Name = "first same";
        session.Observable.MaybeChild!.Name = "second same";
        var parentTransitions = 0;
        var parentNotifications = 0;
        session.TransitionObserved += _ => parentTransitions++;
        session.PropertyChanged += (_, _) => parentNotifications++;
        var transitionsBeforeAccept = parentTransitions;
        var notificationsBeforeAccept = parentNotifications;

        var accepted = session.TryAcceptChildSessions(out var conflicts);
        accepted.ShouldBeTrue(
            $"Unexpected conflicts: {string.Join(", ", conflicts?.Select(static conflict => conflict.PathText) ?? [])}"
        );

        conflicts.ShouldBeNull();
        ReferenceEquals(model.Child, firstModel).ShouldBeTrue();
        ReferenceEquals(model.MaybeChild, secondModel).ShouldBeTrue();
        model.Child.Name.ShouldBe("first same");
        model.MaybeChild!.Name.ShouldBe("second same");
        first.HasChanges.ShouldBeFalse();
        second.HasChanges.ShouldBeFalse();
        first.Session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        second.Session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        firstHistory.CanUndo.ShouldBeFalse();
        secondHistory.CanUndo.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeFalse();
        parentHistory.CanUndo.ShouldBeTrue();
        parentTransitions.ShouldBe(transitionsBeforeAccept);
        parentNotifications.ShouldBe(notificationsBeforeAccept);
    }

    [Test]
    public void AcceptingOneConvergedSiblingAlongsideAChangedSiblingAcknowledgesBoth()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before" },
            MaybeChild = new ObservableChild { Name = "before" },
        };
        var session = model.CreateEditSession();
        var first = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var second = session.OpenChildSession(ObservableHolder.SparsePath.MaybeChild);
        var firstHistory = first.Session.EnableHistory();
        var secondHistory = second.Session.EnableHistory();
        first.Session.Observable.Name = "same";
        second.Session.Observable.Name = "accepted";
        session.Observable.Child!.Name = "same";

        session.TryAcceptChildSessions(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Child.Name.ShouldBe("same");
        model.MaybeChild!.Name.ShouldBe("accepted");
        first.HasChanges.ShouldBeFalse();
        second.HasChanges.ShouldBeFalse();
        first.Session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        second.Session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        firstHistory.CanUndo.ShouldBeFalse();
        secondHistory.CanUndo.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeFalse();
    }

    [Test]
    public void AcceptingConvergedGrandchildAcknowledgesItsHistoryAndPendingState()
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
        var serverHistory = server.Session.EnableHistory();
        server.Session.Observable.Name = "same";
        session.Observable.Groups[0].Servers[0].Name = "same";

        session.TryAcceptChildSessions(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Groups[0].Servers[0].Name.ShouldBe("same");
        server.HasChanges.ShouldBeFalse();
        server.Session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        serverHistory.CanUndo.ShouldBeFalse();
        group.HasChanges.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeFalse();
    }

    [Test]
    public void TypedConflictPreservesParentAndChildStateWithoutAnAcceptanceTransition()
    {
        var model = new ObservableHolder { Child = new ObservableChild { Name = "before" } };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "child";
        session.Observable.Child!.Name = "parent";
        var parentHistory = session.EnableHistory();
        var parentTransitions = 0;
        session.TransitionObserved += _ => parentTransitions++;
        var transitionsBeforeAccept = parentTransitions;
        var parentChangeSet = session.PreviewChangeSet();
        var childChangeSet = child.Session.CreateChangeSet();

        session.TryAcceptChildSessions(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        conflicts!.Select(static conflict => conflict.PathText).ShouldContain("Child.Name");
        session
            .PreviewChangeSet()
            .EnumerateChangedPaths()
            .ShouldBe(parentChangeSet.EnumerateChangedPaths());
        child
            .Session.CreateChangeSet()
            .EnumerateChangedPaths()
            .ShouldBe(childChangeSet.EnumerateChangedPaths());
        model.Child.Name.ShouldBe("parent");
        child.Session.Observable.Name.ShouldBe("child");
        child.HasChanges.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();
        parentHistory.CanUndo.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeTrue();
        parentTransitions.ShouldBe(transitionsBeforeAccept);
    }

    [Test]
    public void LateSetterFailureRollsBackModelButKeepsChildDraftUnacknowledged()
    {
        var firstChild = new ChildAcceptanceFaultModel { Name = "first before", Version = 1 };
        var childModel = new ChildAcceptanceFaultModel { Name = "before", Version = 1 };
        var model = new ChildAcceptanceFaultHolder { First = firstChild, Child = childModel };
        var session = model.CreateEditSession();
        var parentHistory = session.EnableHistory();
        var first = session.OpenChildSession(ChildAcceptanceFaultHolder.SparsePath.First);
        var child = session.OpenChildSession(ChildAcceptanceFaultHolder.SparsePath.Child);
        first.Session.Observable.Name = "first draft";
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "draft";
        child.Session.Observable.Version = 2;
        model.FailAfterChildVersionWrite(2);
        var firstWritesBeforeCommit = model.GetFirstWriteCount();

        var failure = Should.Throw<InvalidOperationException>(() =>
        {
            session.TryAcceptChildSessions(out _);
        });

        failure.Message.ShouldBe("Injected late setter failure.");
        model.First.Name.ShouldBe("first before");
        model.GetFirstWriteCount().ShouldBe(firstWritesBeforeCommit + 1);
        model.Child.Name.ShouldBe("before");
        model.Child.Version.ShouldBe(1);
        first.HasChanges.ShouldBeTrue();
        first.Session.Observable.Name.ShouldBe("first draft");
        child.Session.Observable.Name.ShouldBe("draft");
        child.Session.Observable.Version.ShouldBe(2);
        child.HasChanges.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();
        session.HasPendingChildSessions.ShouldBeTrue();
        parentHistory.CanUndo.ShouldBeTrue();
    }

    [Test]
    public void DescendantCandidateFailurePreservesDraftsBeforeParentCommit()
    {
        var model = new ChildAcceptanceFaultHolder
        {
            Child = new ChildAcceptanceFaultModel { Name = "before", Version = 1 },
        };
        var session = model.CreateEditSession();
        var parentHistory = session.EnableHistory();
        var child = session.OpenChildSession(ChildAcceptanceFaultHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "draft";
        child.Session.Observable.Version = 97;
        ChildAcceptanceFaultModel.FailAfterAnyVersionWrite(97);

        var failure = Should.Throw<InvalidOperationException>(() =>
        {
            session.TryAcceptChildSessions(out _);
        });

        failure.Message.ShouldBe("Injected descendant setter failure.");
        model.Child.Name.ShouldBe("before");
        model.Child.Version.ShouldBe(1);
        child.Session.Observable.Name.ShouldBe("draft");
        child.Session.Observable.Version.ShouldBe(97);
        child.HasChanges.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();
        session.HasPendingChildSessions.ShouldBeTrue();
        parentHistory.CanUndo.ShouldBeFalse();
    }

    [Test]
    public void RollbackFailureReportsBothFailuresAndLeavesBestEffortPartialState()
    {
        var childModel = new ChildAcceptanceFaultModel { Name = "before", Version = 1 };
        var model = new ChildAcceptanceFaultHolder { Child = childModel };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ChildAcceptanceFaultHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "draft";
        child.Session.Observable.Version = 2;
        model.FailAfterChildVersionWrite(2);
        model.FailBeforeChildVersionWrite(1);

        var failure = Should.Throw<AggregateException>(() =>
        {
            session.TryAcceptChildSessions(out _);
        });

        failure.Message.ShouldContain(
            "The child session commit failed and restoring the pre-commit model failed."
        );
        failure.InnerExceptions.Count.ShouldBe(2);
        failure.InnerExceptions[0].Message.ShouldBe("Injected late setter failure.");
        failure.InnerExceptions[1].Message.ShouldBe("Injected rollback failure.");
        model.Child.Name.ShouldBe("draft");
        model.Child.Version.ShouldBe(2);
        child.Session.Observable.Name.ShouldBe("draft");
        child.Session.Observable.Version.ShouldBe(2);
        child.HasChanges.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();
        session.HasPendingChildSessions.ShouldBeTrue();
    }
}

[SparseModel]
public partial class ChildAcceptanceFaultHolder
{
    private ChildAcceptanceFaultModel _child = new();
    private ChildAcceptanceFaultModel _first = new();
    private int _firstWriteCount;
    private int? _throwBeforeChildVersion;
    private int? _throwAfterChildVersion;

    public ChildAcceptanceFaultModel First
    {
        get => _first;
        set
        {
            _first = value;
            _firstWriteCount++;
        }
    }

    public ChildAcceptanceFaultModel Child
    {
        get => _child;
        set
        {
            if (_throwBeforeChildVersion == value.Version)
            {
                _throwBeforeChildVersion = null;
                throw new InvalidOperationException("Injected rollback failure.");
            }

            _child = value;
            if (_throwAfterChildVersion == value.Version)
            {
                _throwAfterChildVersion = null;
                throw new InvalidOperationException("Injected late setter failure.");
            }
        }
    }

    public void FailAfterChildVersionWrite(int value) => _throwAfterChildVersion = value;

    public void FailBeforeChildVersionWrite(int value) => _throwBeforeChildVersion = value;

    public int GetFirstWriteCount() => _firstWriteCount;
}

[SparseModel]
public partial class ChildAcceptanceFaultModel
{
    private static int? _throwAfterAnyVersionWrite;
    private int _version;

    public string Name { get; set; } = string.Empty;

    public int Version
    {
        get => _version;
        set
        {
            _version = value;
            if (_throwAfterAnyVersionWrite == value)
            {
                _throwAfterAnyVersionWrite = null;
                throw new InvalidOperationException("Injected descendant setter failure.");
            }
        }
    }

    public static void FailAfterAnyVersionWrite(int value) => _throwAfterAnyVersionWrite = value;
}
