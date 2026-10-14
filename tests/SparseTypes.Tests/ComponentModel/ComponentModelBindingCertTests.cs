using System.Collections.Specialized;
using System.ComponentModel;

namespace SparseTypes.Tests;

/// <summary>
/// Framework-neutral ComponentModel binding certification (issue #262).
/// Covers the binding surface desktop frameworks consume: scalar and nested
/// <see cref="INotifyPropertyChanged"/> notifications, collection
/// notifications, typed paths, semantic dirty state, and keyed reorder.
/// Child-session lifecycle, bound-target boundaries, validation display,
/// disposal, and threading live in <c>ComponentModelChildSessionCertTests</c>.
/// </summary>
public sealed class ComponentModelBindingCertTests
{
    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, args) => names.Add(args.PropertyName ?? "<null>");
        return names;
    }

    [Test]
    public void ScalarSetRaisesPropertyChangedWithMemberName()
    {
        var model = new ObservableHolder { Title = "a" };
        var session = model.CreateEditSession();
        var names = Watch(session.Observable);

        session.Observable.Title.ShouldBe("a");
        session.Observable.Title = "b";

        model.Title.ShouldBe("b");
        names.ShouldBe([nameof(ObservableHolder.Title)]);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void SameValueAssignmentRaisesNothing()
    {
        var model = new ObservableHolder { Title = "a" };
        var session = model.CreateEditSession();
        var names = Watch(session.Observable);

        session.Observable.Title = "a";

        names.ShouldBeEmpty();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void NestedProxyEditNotifiesRootAndWritesThrough()
    {
        var model = new ObservableHolder { Child = new ObservableChild { Name = "n" } };
        var session = model.CreateEditSession();
        var names = Watch(session.Observable);
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        session.Observable.Child!.Name = "n2";

        model.Child.Name.ShouldBe("n2");
        names.ShouldBe([nameof(ObservableHolder.Child)]);
        transitions.ShouldBe(1);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void NestedReplacementRebuildsChildProxy()
    {
        var model = new ObservableHolder { Child = new ObservableChild { Name = "old" } };
        var session = model.CreateEditSession();
        var names = Watch(session.Observable);

        var first = session.Observable.Child;
        session.Observable.Child = new ChildProxy(new ObservableChild { Name = "new" });

        model.Child.Name.ShouldBe("new");
        ReferenceEquals(first, session.Observable.Child).ShouldBeFalse();
        names.ShouldBe([nameof(ObservableHolder.Child)]);
        session.FieldState(ObservableHolder.SparsePath.Child.Name).IsChanged.ShouldBeTrue();
    }

    [Test]
    public void ListViewRaisesCollectionAndParentNotifications()
    {
        var model = new ObservableHolder { Tags = ["a"] };
        var session = model.CreateEditSession();
        var names = Watch(session.Observable);
        var actions = new List<NotifyCollectionChangedAction>();
        var viewNames = new List<string?>();
        session.Observable.Tags!.CollectionChanged += (_, args) => actions.Add(args.Action);
        session.Observable.Tags.PropertyChanged += (_, args) => viewNames.Add(args.PropertyName);

        session.Observable.Tags.Add("b");

        model.Tags.ShouldBe(["a", "b"]);
        actions.ShouldBe([NotifyCollectionChangedAction.Add]);
        viewNames.ShouldContain("Count");
        viewNames.ShouldContain("Item[]");
        names.ShouldBe([nameof(ObservableHolder.Tags)]);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void DictionaryViewRaisesCollectionNotifications()
    {
        var model = new ObservableHolder { Metadata = new() { ["a"] = "one" } };
        var session = model.CreateEditSession();
        var names = Watch(session.Observable);
        var actions = new List<NotifyCollectionChangedAction>();
        session.Observable.Metadata!.CollectionChanged += (_, args) => actions.Add(args.Action);

        session.Observable.Metadata.Add("b", "two");
        session.Observable.Metadata["a"] = "updated";
        session.Observable.Metadata.Remove("b").ShouldBeTrue();

        model.Metadata.ShouldBe(new Dictionary<string, string> { ["a"] = "updated" });
        actions.ShouldBe([
            NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Replace,
            NotifyCollectionChangedAction.Remove,
        ]);
        names.ShouldBe([
            nameof(ObservableHolder.Metadata),
            nameof(ObservableHolder.Metadata),
            nameof(ObservableHolder.Metadata),
        ]);
    }

    [Test]
    public void PlainModelCollectionEditsBypassViewNotifications()
    {
        var model = new ObservableHolder { Tags = ["a"] };
        var session = model.CreateEditSession();
        var names = Watch(session.Observable);
        var events = 0;
        session.Observable.Tags!.CollectionChanged += (_, _) => events++;

        model.Tags.Add("external");

        session.Observable.Tags.ShouldContain("external");
        names.ShouldBeEmpty();
        events.ShouldBe(0);
    }

    [Test]
    public void TypedPathsResolveFieldAndCollectionState()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Label = "dirty";

        var label = session.FieldState(Settings.SparsePath.Label);
        label.Path.ShouldBe(SparsePath.Root<Settings>().Member("Label"));
        label.IsChanged.ShouldBeTrue();
        label.Before.ShouldBe(Optional<string?>.Present("default"));
        label.After.ShouldBe(Optional<string?>.Present("dirty"));

        session.FieldState(Settings.SparsePath.RetryCount).HasChanges.ShouldBeFalse();
        session.EnumerateChangedPaths().Select(static path => path.ToString()).ShouldBe(["Label"]);
    }

    [Test]
    public void EditThenRestoreIsSemanticallyClean()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);

        session.Observable.Label = "dirty";
        watcher.IsChanged.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();

        session.Observable.Label = "default";

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        watcher.IsChanged.ShouldBeFalse();
        watcher.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void BatchEditPublishesOneSessionNotification()
    {
        var session = new Settings().CreateEditSession();
        var notifications = 0;
        ((INotifyPropertyChanged)session).PropertyChanged += (_, _) => notifications++;

        session.BatchEdit(() =>
        {
            session.Observable.Label = "one";
            session.Observable.Label = "two";
            session.Observable.RetryCount = 9;
        });

        notifications.ShouldBe(1);
        session.HasChanges.ShouldBeTrue();
        session
            .FieldState(Settings.SparsePath.Label)
            .After.ShouldBe(Optional<string?>.Present("two"));
    }

    [Test]
    public void KeyedReorderKeepsIdentityAcrossMoves()
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

        session.Observable.Items.Move(1, 0);

        session.Observable.Items[0].Id.ShouldBe("second");
        session.Observable.Items[1].Id.ShouldBe("first");
        var view = session.CollectionState(KeyedServerHolder.SparsePath.Items);
        view.OrderChanged.ShouldBeTrue();
        view.GetItemState("second").IsReordered.ShouldBeTrue();

        // Keyed paths address identity, not position.
        session.Observable.Items.First(item => item.Id == "second").Name = "updated";
        model.Items.First(item => item.Id == "second").Name.ShouldBe("updated");
        session
            .FieldState(KeyedServerHolder.SparsePath.Items.Key("second").Name)
            .IsChanged.ShouldBeTrue();
    }

    [Test]
    public void KeyedRowEditReportsEditedStateWithoutReorder()
    {
        var session = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "a", Name = "a" },
                new KeyedServer { Id = "b", Name = "b" },
            ],
        }.CreateEditSession();

        session.Observable.Items[1].Name = "b2";

        var view = session.CollectionState(KeyedServerHolder.SparsePath.Items);
        view.OrderChanged.ShouldBeFalse();
        view.GetItemState("b").IsEdited.ShouldBeTrue();
        view.GetItemState("b").IsReordered.ShouldBeFalse();
        view.GetItemState("a").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ObservableSurfacesBindingInterfacesWithoutEditingContracts()
    {
        var session = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "a", Name = "a" }],
        }.CreateEditSession();

        ((INotifyPropertyChanged)session.Observable).ShouldNotBeNull();
        session.Observable.Items.ShouldBeAssignableTo<System.Collections.IList>();
        session.Observable.Items.ShouldBeAssignableTo<INotifyCollectionChanged>();

        // Desktop grids bind these standard interfaces. Row currency and
        // multi-cycle row editing contracts stay out on purpose: no
        // IBindingList adapter and no IEditableObject wrapper are provided
        // without demonstrated consumer evidence.
        session
            .Observable.Items.GetType()
            .GetInterfaces()
            .ShouldNotContain(typeof(IBindingList));
        session.Observable.GetType().GetInterfaces().ShouldNotContain(typeof(INotifyDataErrorInfo));
        session.Observable.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
    }
}
