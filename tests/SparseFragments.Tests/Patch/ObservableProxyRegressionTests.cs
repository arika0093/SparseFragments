using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SparseFragments.Tests;

// Regression coverage for issue #114: targeted pruning must preserve proxy
// identity, nested lifetime, single event delivery, disposal teardown, and
// snapshot detection of direct non-notifying edits.
public sealed class ObservableProxyRegressionTests
{
    private sealed class ProbeChild
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ProbeView
    {
        public ProbeView(ProbeChild target, Action changed)
        {
            Target = target;
            Changed = changed;
        }

        public ProbeChild Target { get; }

        public Action Changed { get; }
    }

    private static SparseObservableList<ProbeChild, ProbeView> ProbeList(
        IList<ProbeChild> models,
        Action onChanged,
        bool cacheReferences = true
    ) =>
        new(
            models,
            static (item, changed) => new ProbeView(item, changed),
            static view => view.Target,
            onChanged,
            cacheReferences
        );

    private static SparseObservableDictionary<string, ProbeChild, ProbeView> ProbeDict(
        IDictionary<string, ProbeChild> models,
        Action onChanged
    ) =>
        new(
            models,
            static (item, changed) => new ProbeView(item, changed),
            static view => view.Target,
            onChanged,
            cacheReferences: true
        );

    [Test]
    public void ListProxyIdentityIsStableAcrossAccessPatterns()
    {
        var model = new ObservableHolder { Children = [new() { Id = "a" }, new() { Id = "b" }] };
        var proxy = new ObservableHolder.Observable(model);
        var view = proxy.Children!;
        var first = view[0];
        var copy = new ObservableListChild.SparseObservable[view.Count];
        view.CopyTo(copy, 0);

        ReferenceEquals(first, view[0]).ShouldBeTrue();
        ReferenceEquals(first, copy[0]).ShouldBeTrue();
        view.ShouldContain(first);
    }

    [Test]
    public void ListRemoveDetachesOnlyEvictedProxy()
    {
        var notified = 0;
        var model = new ObservableHolder { Children = [new() { Id = "a" }, new() { Id = "b" }] };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var view = proxy.Children!;
        var first = view[0];
        var second = view[1];

        view.RemoveAt(0);

        ReferenceEquals(second, view[0]).ShouldBeTrue();
        second.Name = "kept";
        notified.ShouldBe(2);
        first.Name = "detached";
        notified.ShouldBe(2);
    }

    [Test]
    public void ListDuplicateReferenceStaysAliveUntilLastRemoval()
    {
        var notified = 0;
        var shared = new ProbeChild { Name = "shared" };
        var view = ProbeList([shared, shared], () => notified++);
        var first = view[0];

        ReferenceEquals(first, view[1]).ShouldBeTrue();
        view.RemoveAt(0);
        notified.ShouldBe(1);
        first.Changed();
        notified.ShouldBe(2);

        view.RemoveAt(0);
        notified.ShouldBe(3);
        first.Changed();
        notified.ShouldBe(3);
    }

    [Test]
    public void ListClearDetachesAllWithSingleReset()
    {
        var notified = 0;
        var view = ProbeList(
            [new ProbeChild { Name = "a" }, new ProbeChild { Name = "b" }],
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var first = view[0];

        view.Clear();

        events.Select(args => args.Action).ShouldBe([NotifyCollectionChangedAction.Reset]);
        notified.ShouldBe(1);
        first.Changed();
        notified.ShouldBe(1);
    }

    [Test]
    public void ListReplaceDetachesOnlyOldProxy()
    {
        var notified = 0;
        var view = ProbeList(
            [new ProbeChild { Name = "old" }, new ProbeChild { Name = "keep" }],
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var old = view[0];
        var kept = view[1];

        view[0] = new ProbeView(new ProbeChild { Name = "new" }, () => { });

        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Replace);
        ReferenceEquals(kept, view[1]).ShouldBeTrue();
        kept.Changed();
        notified.ShouldBe(2);
        old.Changed();
        notified.ShouldBe(2);
    }

    [Test]
    public void ListMoveKeepsProxiesAliveAndRaisesOnce()
    {
        var notified = 0;
        var view = ProbeList(
            [new ProbeChild { Name = "a" }, new ProbeChild { Name = "b" }],
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var first = view[0];
        var second = view[1];

        view.Move(0, 1);

        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Move);
        ReferenceEquals(first, view[1]).ShouldBeTrue();
        ReferenceEquals(second, view[0]).ShouldBeTrue();
        first.Changed();
        notified.ShouldBe(2);
    }

    [Test]
    public void ListAddAndInsertDoNotDisturbCachedProxies()
    {
        var notified = 0;
        var view = ProbeList([new ProbeChild { Name = "a" }], () => notified++);
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var first = view[0];

        view.AddModel(new ProbeChild { Name = "b" });
        view.InsertModel(0, new ProbeChild { Name = "front" });

        ReferenceEquals(first, view[1]).ShouldBeTrue();
        events
            .Select(args => args.Action)
            .ShouldBe([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add]);
        first.Changed();
        notified.ShouldBe(3);
    }

    [Test]
    public void ExternalNotifyingRemovePrunesAndDeliversOnce()
    {
        var notified = 0;
        var source = new ObservableCollection<ProbeChild>
        {
            new() { Name = "a" },
            new() { Name = "b" },
        };
        var view = ProbeList(source, () => notified++);
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var first = view[0];
        var second = view[1];

        source.RemoveAt(0);

        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Remove);
        notified.ShouldBe(1);
        ReferenceEquals(second, view[0]).ShouldBeTrue();
        second.Changed();
        notified.ShouldBe(2);
        first.Changed();
        notified.ShouldBe(2);
    }

    [Test]
    public void ExternalNotifyingAddKeepsCachedProxies()
    {
        var notified = 0;
        var source = new ObservableCollection<ProbeChild> { new() { Name = "a" } };
        var view = ProbeList(source, () => notified++);
        var first = view[0];

        source.Add(new ProbeChild { Name = "b" });

        ReferenceEquals(first, view[0]).ShouldBeTrue();
        notified.ShouldBe(1);
        first.Changed();
        notified.ShouldBe(2);
    }

    [Test]
    public void DisposedListViewDetachesProxiesAndUnsubscribes()
    {
        var notified = 0;
        var source = new ObservableCollection<ProbeChild> { new() { Name = "a" } };
        var view = ProbeList(source, () => notified++);
        var events = 0;
        view.CollectionChanged += (_, _) => events++;
        var first = view[0];

        view.Dispose();
        view.IsDisposed.ShouldBeTrue();
        view.Dispose();

        first.Changed();
        source.Add(new ProbeChild { Name = "late" });
        notified.ShouldBe(0);
        events.ShouldBe(0);
        view.Count.ShouldBe(2);
        Should.Throw<ObjectDisposedException>(() => view.AddModel(new ProbeChild()));
    }

    [Test]
    public void DirectModelEditRaisesNoEventsButViewReflectsContent()
    {
        var notified = 0;
        var model = new ObservableHolder { Tags = ["a"] };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var events = 0;
        proxy.Tags!.CollectionChanged += (_, _) => events++;

        model.Tags.Add("external");

        events.ShouldBe(0);
        notified.ShouldBe(0);
        proxy.Tags.Count.ShouldBe(2);
        proxy.Tags.ShouldContain("external");
    }

    [Test]
    public void SessionDetectsDirectDictionaryEditWithoutEvents()
    {
        var model = new NeutralSessionModel();
        var session = model.CreateEditSession();
        var events = 0;
        session.Observable.Counts.CollectionChanged += (_, _) => events++;

        model.Counts["direct"] = 1;

        events.ShouldBe(0);
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Counts.IsChanged.ShouldBeTrue();
    }

    [Test]
    public void DictionaryProxyIdentityIsStable()
    {
        var model = new ObservableHolder { ChildrenByName = new() { ["a"] = new() { Id = "a" } } };
        var proxy = new ObservableHolder.Observable(model);
        var view = proxy.ChildrenByName!;

        view.TryGetValue("a", out var first).ShouldBeTrue();
        ReferenceEquals(first, view["a"]).ShouldBeTrue();
    }

    [Test]
    public void DictionaryRemoveDetachesOnlyEvictedValue()
    {
        var notified = 0;
        var first = new ProbeChild { Name = "a" };
        var second = new ProbeChild { Name = "b" };
        var view = ProbeDict(
            new Dictionary<string, ProbeChild> { ["a"] = first, ["b"] = second },
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var firstProxy = view["a"];
        var secondProxy = view["b"];

        view.Remove("a").ShouldBeTrue();

        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Remove);
        ReferenceEquals(secondProxy, view["b"]).ShouldBeTrue();
        secondProxy.Changed();
        notified.ShouldBe(2);
        firstProxy.Changed();
        notified.ShouldBe(2);
    }

    [Test]
    public void DictionaryDuplicateValueStaysAliveUntilLastKeyRemoved()
    {
        var notified = 0;
        var shared = new ProbeChild { Name = "shared" };
        var view = ProbeDict(
            new Dictionary<string, ProbeChild> { ["a"] = shared, ["b"] = shared },
            () => notified++
        );
        var sharedProxy = view["a"];

        ReferenceEquals(sharedProxy, view["b"]).ShouldBeTrue();
        view.Remove("a").ShouldBeTrue();
        notified.ShouldBe(1);
        sharedProxy.Changed();
        notified.ShouldBe(2);

        view.Remove("b").ShouldBeTrue();
        notified.ShouldBe(3);
        sharedProxy.Changed();
        notified.ShouldBe(3);
    }

    [Test]
    public void DictionarySetReplaceDetachesOnlyOldValue()
    {
        var notified = 0;
        var view = ProbeDict(
            new Dictionary<string, ProbeChild> { ["a"] = new() { Name = "old" } },
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var old = view["a"];

        view["a"] = new ProbeView(new ProbeChild { Name = "new" }, () => { });

        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Replace);
        notified.ShouldBe(1);
        old.Changed();
        notified.ShouldBe(1);
    }

    [Test]
    public void DictionarySetSameReferenceIsSuppressed()
    {
        var notified = 0;
        var model = new ObservableHolder { ChildrenByName = new() { ["a"] = new() { Id = "a" } } };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var view = proxy.ChildrenByName!;
        var current = view["a"];

        view["a"] = current;

        notified.ShouldBe(0);
        ReferenceEquals(current, view["a"]).ShouldBeTrue();
    }

    [Test]
    public void DictionaryClearDetachesAllWithSingleReset()
    {
        var notified = 0;
        var view = ProbeDict(
            new Dictionary<string, ProbeChild> { ["a"] = new() { Name = "a" } },
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var first = view["a"];

        view.Clear();

        events.Select(args => args.Action).ShouldBe([NotifyCollectionChangedAction.Reset]);
        first.Changed();
        notified.ShouldBe(1);
    }

    [Test]
    public void DisposedDictionaryViewDetachesProxies()
    {
        var notified = 0;
        var view = ProbeDict(
            new Dictionary<string, ProbeChild> { ["a"] = new() { Name = "a" } },
            () => notified++
        );
        var first = view["a"];

        view.Dispose();
        view.IsDisposed.ShouldBeTrue();

        first.Changed();
        notified.ShouldBe(0);
        Should.Throw<ObjectDisposedException>(() => view.Remove("a"));
    }

    [Test]
    public void ReplacingDictionaryRebuildsViewAndDetachesPreviousValues()
    {
        var notified = 0;
        var model = new ObservableHolder { ChildrenByName = new() { ["a"] = new() { Id = "a" } } };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var oldView = proxy.ChildrenByName!;
        var oldElement = oldView["a"];

        proxy.ReplaceChildrenByName(new() { ["b"] = new() { Id = "b" } });

        ReferenceEquals(oldView, proxy.ChildrenByName).ShouldBeFalse();
        notified.ShouldBe(1);
        oldElement.Name = "stale";
        notified.ShouldBe(1);
        Should.Throw<ObjectDisposedException>(() => oldView.Remove("b"));
    }
}
