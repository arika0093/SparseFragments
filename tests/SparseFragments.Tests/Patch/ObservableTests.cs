using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using SparseFragments.Generated;

namespace SparseFragments.Tests;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PasswordAttribute : Attribute
{
    public PasswordAttribute(string scope) => Scope = scope;

    public string Scope { get; set; }
}

[SparseFragmentModel]
public partial class ObservableChild
{
    public string Name { get; set; } = string.Empty;

    public string Observable { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial struct ObservableSpot
{
    public int X { get; set; }

    public int Y { get; set; }
}

[SparseFragmentModel]
public partial class ObservableListChild
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Observable { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class ObservableHolder
{
    public string Title { get; set; } = string.Empty;

    [Password("login", Scope = "credential")]
    public string Secret { get; set; } = string.Empty;

    public string? Note { get; set; }

    public ObservableChild Child { get; set; } = new();

    public ObservableChild? MaybeChild { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public ObservableChild? ReplaceChild { get; set; } = new();

    public ObservableSpot Spot { get; set; }

    public List<string> Tags { get; set; } = new();

    [SparseMerge(MergeMode.Replace)]
    public List<string> ReplacementTags { get; set; } = new();

    public List<string?> NullableTags { get; set; } = new();

    public List<ObservableListChild> Children { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } = new();

    public Dictionary<string, string?> NullableMetadata { get; set; } = new();

    public IList<string> InterfaceTags { get; set; } = new List<string>();

    public IDictionary<string, string> InterfaceMetadata { get; set; } =
        new Dictionary<string, string>();

    public Dictionary<string, ObservableListChild> ChildrenByName { get; set; } = new();

    [SparseCloneReferenceSafe]
    public ObservableCollection<string> LiveTags { get; set; } = new();

    [SparseCloneReferenceSafe]
    public Collection<string> LegacyTags { get; set; } = new();

    public string[] Labels { get; set; } = [];

    public int OwningCount { get; init; }
}

[SparseFragmentModel]
public partial class ObservablePrivateAttributeModel
{
    [AttributeUsage(AttributeTargets.Property)]
    private sealed class PrivateMarkerAttribute : Attribute;

    [PrivateMarker]
    public string Value { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class ObservableNode
{
    public int Value { get; set; }

    public ObservableNode? Next { get; set; }
}

[SparseFragmentModel]
public partial class ObservableCollision
{
    public string Observable { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PropertyChanged { get; set; } = string.Empty;
}

public sealed class ObservableTests
{
    private static List<string> Events(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, args) => names.Add(args.PropertyName ?? "<null>");
        return names;
    }

    [Test]
    public void ScalarGetSetAndNotification()
    {
        var model = new ObservableHolder { Title = "a" };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Title.ShouldBe("a");
        proxy.Title = "b";
        model.Title.ShouldBe("b");
        names.ShouldBe(["Title"]);
    }

    [Test]
    public void EqualitySuppression()
    {
        var model = new ObservableHolder { Title = "a" };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Title = "a";
        names.ShouldBeEmpty();
        model.Title = "b";
        proxy.Note = null;
        names.ShouldBeEmpty();
    }

    [Test]
    public void NestedProxyCachingAndPropagation()
    {
        var notified = 0;
        var model = new ObservableHolder { Child = new ObservableChild { Name = "n" } };
        var proxy = new ObservableHolder.Observable(model, () => notified++);

        ReferenceEquals(proxy.Child, proxy.Child).ShouldBeTrue();
        var names = Events(proxy);

        proxy.Child!.Name = "n2";
        model.Child.Name.ShouldBe("n2");
        notified.ShouldBe(1);
        names.ShouldBe(["Child"]);
    }

    [Test]
    public void NestedReplacementRebuildsProxy()
    {
        var notified = 0;
        var model = new ObservableHolder { Child = new ObservableChild { Name = "old" } };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var names = Events(proxy);

        var first = proxy.Child;
        proxy.Child = new ObservableChild.SparseObservable(new ObservableChild { Name = "new" });
        model.Child.Name.ShouldBe("new");
        ReferenceEquals(first, proxy.Child).ShouldBeFalse();
        proxy.Child!.Name.ShouldBe("new");
        names.ShouldBe(["Child"]);
        notified.ShouldBe(1);
    }

    [Test]
    public void NullNestedPassthrough()
    {
        var model = new ObservableHolder { MaybeChild = null };
        var proxy = new ObservableHolder.Observable(model);

        proxy.MaybeChild.ShouldBeNull();

        proxy.MaybeChild = new ObservableChild.SparseObservable(new ObservableChild { Name = "x" });
        model.MaybeChild!.Name.ShouldBe("x");
        proxy.MaybeChild!.Name.ShouldBe("x");

        proxy.MaybeChild = null;
        model.MaybeChild.ShouldBeNull();
        proxy.MaybeChild.ShouldBeNull();
    }

    [Test]
    public void InitAndReadOnlyMembers()
    {
        var model = new ObservableHolder { OwningCount = 3, Title = "t" };
        var proxy = new ObservableHolder.Observable(model);

        proxy.OwningCount.ShouldBe(3);
        proxy.Title = "t2";
        model.Title.ShouldBe("t2");
    }

    [Test]
    public void SessionDescriptorsReadAndEditScalarPropertiesThroughObservable()
    {
        var model = new ObservableHolder { Secret = "before" };
        var session = model.CreateEditSession();
        var notifications = Events(session.Observable);

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Secret), out var descriptor)
            .ShouldBeTrue();
        descriptor.Path.ShouldBe(nameof(ObservableHolder.Secret));
        descriptor.Type.ShouldBe(typeof(string));
        descriptor.IsNullable.ShouldBeFalse();
        descriptor.IsEditable.ShouldBeTrue();
        descriptor.IsReadOnly.ShouldBeFalse();
        descriptor.GetValue().ShouldBe("before");
        descriptor.TrySetValue(null).ShouldBeFalse();
        var password = descriptor.Attributes.OfType<PasswordAttribute>().SingleOrDefault();
        password.ShouldNotBeNull();
        password!.Scope.ShouldBe("credential");

        descriptor.TrySetValue("after").ShouldBeTrue();
        model.Secret.ShouldBe("after");
        session.HasChanges.ShouldBeTrue();
        notifications.ShouldContain(nameof(ObservableHolder.Secret));

        session
            .Descriptors.TryGet(nameof(ObservableHolder.OwningCount), out var initDescriptor)
            .ShouldBeTrue();
        initDescriptor.IsEditable.ShouldBeFalse();
        initDescriptor.IsReadOnly.ShouldBeTrue();
        initDescriptor.TrySetValue(10).ShouldBeFalse();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Note), out var nullableDescriptor)
            .ShouldBeTrue();
        nullableDescriptor.IsNullable.ShouldBeTrue();
        nullableDescriptor.TrySetValue(null).ShouldBeTrue();
        model.Note.ShouldBeNull();
    }

    [Test]
    public void DescriptorsCanMaterializePrivateNestedPropertyAttributes()
    {
        var session = new ObservablePrivateAttributeModel().CreateEditSession();

        session
            .Descriptors.Members.Single()
            .Attributes.Single()
            .GetType()
            .Name.ShouldBe("PrivateMarkerAttribute");
    }

    [Test]
    public void NestedDescriptorsCarryPathsAndEditThroughChildObservable()
    {
        var model = new ObservableHolder { Child = new ObservableChild { Name = "before" } };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Child), out var childDescriptor)
            .ShouldBeTrue();
        var childDescriptors = childDescriptor.Child;
        childDescriptors.ShouldNotBeNull();
        childDescriptors!
            .TryGet(nameof(ObservableChild.Name), out var nameDescriptor)
            .ShouldBeTrue();
        nameDescriptor.Path.ShouldBe("Child.Name");
        nameDescriptor.TrySetValue("after").ShouldBeTrue();

        model.Child.Name.ShouldBe("after");
        session.HasChanges.ShouldBeTrue();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ReplaceChild), out var replaceDescriptor)
            .ShouldBeTrue();
        var replaceChildDescriptors = replaceDescriptor.Child;
        replaceChildDescriptors.ShouldNotBeNull();
        replaceChildDescriptors!
            .TryGet(nameof(ObservableChild.Name), out var replaceChildName)
            .ShouldBeTrue();
        replaceChildName.TrySetValue("replaced").ShouldBeTrue();
        model.ReplaceChild!.Name.ShouldBe("replaced");
    }

    [Test]
    public void CollectionDescriptorsExposeMetadataAndNotifyThroughObservableViews()
    {
        var model = new ObservableHolder
        {
            ReplacementTags = ["a"],
            NullableTags = ["initial"],
            Metadata = new Dictionary<string, string> { ["first"] = "one" },
            NullableMetadata = new Dictionary<string, string?> { ["first"] = "one" },
            Children = [new ObservableListChild { Id = "item", Name = "before" }],
            ChildrenByName = new Dictionary<string, ObservableListChild>
            {
                ["entry"] = new() { Id = "mapped", Name = "before" },
            },
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ReplacementTags), out var tagsDescriptor)
            .ShouldBeTrue();
        var tags = tagsDescriptor.Array;
        tags.ShouldNotBeNull();
        var tagNotifications = new List<NotifyCollectionChangedAction>();
        var observableNotifications = Events(session.Observable);
        session.Observable.ReplacementTags!.CollectionChanged += (_, args) =>
            tagNotifications.Add(args.Action);
        tags!.ItemType.ShouldBe(typeof(string));
        tags.IsItemNullable.ShouldBeFalse();
        tags.CanAdd.ShouldBeTrue();
        tags.CanInsert.ShouldBeTrue();
        tags.CanRemove.ShouldBeTrue();
        tags.CanMove.ShouldBeTrue();
        tags.TryAdd("b").ShouldBeTrue();
        tags.TryInsert(1, "inserted").ShouldBeTrue();
        tags.TryMove(2, 0).ShouldBeTrue();
        tags.TryRemoveAt(2).ShouldBeTrue();
        tags.TryAdd(null).ShouldBeFalse();
        model.ReplacementTags.ShouldBe(["b", "a"]);
        tagNotifications.ShouldBe([
            NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Move,
            NotifyCollectionChangedAction.Remove,
        ]);
        observableNotifications
            .Count(name => name == nameof(ObservableHolder.ReplacementTags))
            .ShouldBe(4);

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Metadata), out var metadataDescriptor)
            .ShouldBeTrue();
        var metadata = metadataDescriptor.Dictionary;
        metadata.ShouldNotBeNull();
        metadata!.KeyType.ShouldBe(typeof(string));
        metadata.ValueType.ShouldBe(typeof(string));
        metadata.IsValueNullable.ShouldBeFalse();
        var dictionaryNotifications = new List<NotifyCollectionChangedAction>();
        session.Observable.Metadata!.CollectionChanged += (_, args) =>
            dictionaryNotifications.Add(args.Action);
        metadata.TryGetValue("first", out var first).ShouldBeTrue();
        first.ShouldBe("one");
        metadata.TryAdd("first", "duplicate").ShouldBeFalse();
        metadata.TrySetValue("missing", "value").ShouldBeFalse();
        metadata.TryAdd("null", null).ShouldBeFalse();
        metadata.TryGetValue(null, out _).ShouldBeFalse();
        metadata.TryAdd(null, "value").ShouldBeFalse();
        metadata.TrySetValue(null, "value").ShouldBeFalse();
        metadata.TryRemove(null).ShouldBeFalse();
        metadata.TryAdd("second", "two").ShouldBeTrue();
        metadata.TrySetValue("first", "updated").ShouldBeTrue();
        metadata.TryRemove("second").ShouldBeTrue();
        model.Metadata.ShouldBe(new Dictionary<string, string> { ["first"] = "updated" });
        dictionaryNotifications.ShouldBe([
            NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Replace,
            NotifyCollectionChangedAction.Remove,
        ]);

        session
            .Descriptors.TryGet(nameof(ObservableHolder.InterfaceTags), out var interfaceTags)
            .ShouldBeTrue();
        interfaceTags.TrySetValue(interfaceTags.GetValue()).ShouldBeTrue();
        interfaceTags.Array!.TryAdd("still-live").ShouldBeTrue();
        model.InterfaceTags.ShouldBe(["still-live"]);

        session
            .Descriptors.TryGet(
                nameof(ObservableHolder.InterfaceMetadata),
                out var interfaceMetadata
            )
            .ShouldBeTrue();
        interfaceMetadata.TrySetValue(interfaceMetadata.GetValue()).ShouldBeTrue();
        interfaceMetadata.Dictionary!.TryAdd("still-live", "value").ShouldBeTrue();
        model.InterfaceMetadata.ShouldBe(
            new Dictionary<string, string> { ["still-live"] = "value" }
        );

        session
            .Descriptors.TryGet(
                nameof(ObservableHolder.NullableTags),
                out var nullableTagsDescriptor
            )
            .ShouldBeTrue();
        var nullableTags = nullableTagsDescriptor.Array;
        nullableTags.ShouldNotBeNull();
        nullableTags!.IsItemNullable.ShouldBeTrue();
        nullableTags.TryAdd(null).ShouldBeTrue();

        session
            .Descriptors.TryGet(
                nameof(ObservableHolder.NullableMetadata),
                out var nullableMetadataDescriptor
            )
            .ShouldBeTrue();
        var nullableMetadata = nullableMetadataDescriptor.Dictionary;
        nullableMetadata.ShouldNotBeNull();
        nullableMetadata!.IsValueNullable.ShouldBeTrue();
        nullableMetadata.TryAdd("null", null).ShouldBeTrue();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var childrenDescriptor)
            .ShouldBeTrue();
        var children = childrenDescriptor.Array;
        children.ShouldNotBeNull();
        var childProxy = children!.GetItem(0);
        children.TrySetItem(0, childProxy).ShouldBeTrue();
        children!
            .GetItemDescriptors(0)!
            .TryGet(nameof(ObservableListChild.Name), out var childName)
            .ShouldBeTrue();
        childName.Path.ShouldBe("Children[0].Name");
        childName.TrySetValue("list-updated").ShouldBeTrue();

        session
            .Descriptors.TryGet(
                nameof(ObservableHolder.ChildrenByName),
                out var childrenByNameDescriptor
            )
            .ShouldBeTrue();
        var childrenByName = childrenByNameDescriptor.Dictionary;
        childrenByName.ShouldNotBeNull();
        childrenByName!.TryGetValue("entry", out var childDictionaryProxy).ShouldBeTrue();
        childrenByName.TrySetValue("entry", childDictionaryProxy).ShouldBeTrue();
        childrenByName!
            .GetValueDescriptors("entry")!
            .TryGet(nameof(ObservableListChild.Name), out var dictionaryChildName)
            .ShouldBeTrue();
        dictionaryChildName.Path.ShouldBe("ChildrenByName[\"entry\"].Name");
        dictionaryChildName.TrySetValue("dictionary-updated").ShouldBeTrue();
        model.Children[0].Name.ShouldBe("list-updated");
        model.ChildrenByName["entry"].Name.ShouldBe("dictionary-updated");

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Labels), out var labelsDescriptor)
            .ShouldBeTrue();
        var labels = labelsDescriptor.Array;
        labels.ShouldNotBeNull();
        labels!.ItemType.ShouldBe(typeof(string));
        labels.Count.ShouldBe(0);
        labels.CanAdd.ShouldBeFalse();
        labels.CanSetItem.ShouldBeFalse();
        labels.TryAdd("unsupported").ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ValueTypeStructuralMember()
    {
        var model = new ObservableHolder
        {
            Spot = new ObservableSpot { X = 1, Y = 2 },
        };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Spot.X.ShouldBe(1);
        proxy.Spot = new ObservableSpot { X = 9, Y = 2 };
        model.Spot.X.ShouldBe(9);
        names.ShouldBe(["Spot"]);
    }

    [Test]
    public void CollectionReplacement()
    {
        var model = new ObservableHolder { Tags = new() { "a" } };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        var same = model.Tags;
        proxy.ReplaceTags(same);
        names.ShouldBeEmpty();

        proxy.ReplaceTags(new() { "b" });
        model.Tags.ShouldBe(["b"]);
        names.ShouldBe(["Tags"]);
    }

    [Test]
    public void PlainModelCollectionMutationsBypassViewNotifications()
    {
        var model = new ObservableHolder { Tags = ["a"] };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);
        var collectionEvents = 0;
        proxy.Tags!.CollectionChanged += (_, _) => collectionEvents++;

        model.Tags.Add("external");

        proxy.Tags.ShouldContain("external");
        names.ShouldBeEmpty();
        collectionEvents.ShouldBe(0);
    }

    [Test]
    public void ListViewMutatesTheOriginalListAndRaisesCollectionAndParentNotifications()
    {
        var notified = 0;
        var source = new List<string> { "a", "b" };
        var model = new ObservableHolder { Tags = source };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var names = Events(proxy);
        var events = new List<NotifyCollectionChangedEventArgs>();
        var viewPropertyNames = new List<string?>();
        proxy.Tags!.CollectionChanged += (_, args) => events.Add(args);
        proxy.Tags.PropertyChanged += (_, args) => viewPropertyNames.Add(args.PropertyName);

        proxy.Tags.Add("c");
        proxy.Tags.Insert(1, "x");
        proxy.Tags[0] = "z";
        proxy.Tags.Move(3, 1);
        proxy.Tags.RemoveAt(0);
        proxy.Tags.Clear();

        ReferenceEquals(source, model.Tags).ShouldBeTrue();
        model.Tags.ShouldBeEmpty();
        events
            .Select(args => args.Action)
            .ShouldBe([
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Replace,
                NotifyCollectionChangedAction.Move,
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Reset,
            ]);
        events[0].NewStartingIndex.ShouldBe(2);
        events[1].NewStartingIndex.ShouldBe(1);
        events[2].OldStartingIndex.ShouldBe(0);
        events[3].OldStartingIndex.ShouldBe(3);
        events[3].NewStartingIndex.ShouldBe(1);
        names.ShouldBe(["Tags", "Tags", "Tags", "Tags", "Tags", "Tags"]);
        notified.ShouldBe(6);
        viewPropertyNames.ShouldBe([
            "Count",
            "Item[]",
            "Count",
            "Item[]",
            "Item[]",
            "Item[]",
            "Count",
            "Item[]",
            "Count",
            "Item[]",
        ]);
    }

    [Test]
    public void ChildListViewsCacheAndDetachElementProxies()
    {
        var notified = 0;
        var first = new ObservableListChild { Id = "a", Name = "A" };
        var model = new ObservableHolder { Children = [first] };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var names = Events(proxy);
        var view = proxy.Children!;
        var firstProxy = view[0];

        ReferenceEquals(firstProxy, view[0]).ShouldBeTrue();
        firstProxy.Name = "updated";
        names.ShouldBe(["Children"]);
        notified.ShouldBe(1);

        view.RemoveAt(0);
        names.ShouldBe(["Children", "Children"]);
        firstProxy.Name = "detached";
        names.ShouldBe(["Children", "Children"]);
        notified.ShouldBe(2);

        var added = new ObservableListChild.SparseObservable(new ObservableListChild { Id = "b" });
        view.Add(added);
        view[0].Name = "active";
        notified.ShouldBe(4);
        names.ShouldBe(["Children", "Children", "Children", "Children"]);
    }

    [Test]
    public void ReplacingListRebuildsViewAndDetachesPreviousElements()
    {
        var notified = 0;
        var child = new ObservableListChild { Id = "a" };
        var model = new ObservableHolder { Children = [child] };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var oldView = proxy.Children!;
        var oldElement = oldView[0];
        var replacement = new List<ObservableListChild> { new() { Id = "b" } };

        proxy.ReplaceChildren(replacement);

        ReferenceEquals(replacement, model.Children).ShouldBeTrue();
        ReferenceEquals(oldView, proxy.Children).ShouldBeFalse();
        notified.ShouldBe(1);
        oldElement.Name = "stale";
        notified.ShouldBe(1);
    }

    [Test]
    public void ObservableCollectionEventsAreForwardedOnce()
    {
        var source = new ObservableCollection<string> { "a" };
        var notified = 0;
        var proxy = new ObservableHolder.Observable(
            new ObservableHolder { LiveTags = source },
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        proxy.LiveTags!.CollectionChanged += (_, args) => events.Add(args);

        proxy.LiveTags.Add("b");
        proxy.LiveTags.Move(1, 0);

        events.Count.ShouldBe(2);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Add);
        events[1].Action.ShouldBe(NotifyCollectionChangedAction.Move);
        notified.ShouldBe(2);
        ReferenceEquals(source, proxy.Model.LiveTags).ShouldBeTrue();
    }

    [Test]
    public void CollectionTypeUsesTheObservableListView()
    {
        var source = new Collection<string> { "a" };
        var proxy = new ObservableHolder.Observable(new ObservableHolder { LegacyTags = source });
        var events = new List<NotifyCollectionChangedEventArgs>();
        proxy.LegacyTags!.CollectionChanged += (_, args) => events.Add(args);

        proxy.LegacyTags.Add("b");

        ReferenceEquals(source, proxy.Model.LegacyTags).ShouldBeTrue();
        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Add);
    }

    [Test]
    public void DictionaryViewNotifiesAndBubblesElementChanges()
    {
        var notified = 0;
        var child = new ObservableListChild { Id = "a", Name = "old" };
        var model = new ObservableHolder
        {
            Metadata = new Dictionary<string, string> { ["a"] = "one" },
            ChildrenByName = new Dictionary<string, ObservableListChild> { ["a"] = child },
        };
        var proxy = new ObservableHolder.Observable(model, () => notified++);
        var metadataEvents = new List<NotifyCollectionChangedEventArgs>();
        var childEvents = new List<NotifyCollectionChangedEventArgs>();
        var dictionaryPropertyNames = new List<string?>();
        proxy.Metadata!.CollectionChanged += (_, args) => metadataEvents.Add(args);
        proxy.Metadata.PropertyChanged += (_, args) =>
            dictionaryPropertyNames.Add(args.PropertyName);
        proxy.ChildrenByName!.CollectionChanged += (_, args) => childEvents.Add(args);
        var names = Events(proxy);

        proxy.Metadata.Add("b", "two");
        proxy.Metadata["a"] = "updated";
        proxy.Metadata.Remove("b").ShouldBeTrue();
        var childProxy = proxy.ChildrenByName["a"];
        childProxy.Name = "new";
        proxy.ChildrenByName.Remove("a").ShouldBeTrue();
        childProxy.Name = "detached";
        proxy.ChildrenByName.Add(
            "b",
            new ObservableListChild.SparseObservable(new ObservableListChild { Id = "b" })
        );
        proxy.ChildrenByName.Clear();

        metadataEvents
            .Select(args => args.Action)
            .ShouldBe([
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Replace,
                NotifyCollectionChangedAction.Remove,
            ]);
        dictionaryPropertyNames.ShouldBe(["Count", "Item[]", "Item[]", "Count", "Item[]"]);
        childEvents
            .Select(args => args.Action)
            .ShouldBe([
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Reset,
            ]);
        names.ShouldBe([
            "Metadata",
            "Metadata",
            "Metadata",
            "ChildrenByName",
            "ChildrenByName",
            "ChildrenByName",
            "ChildrenByName",
        ]);
        notified.ShouldBe(7);
    }

    [Test]
    public void ArrayRemainsReplaceOnly()
    {
        var source = new[] { "a" };
        var proxy = new ObservableHolder.Observable(new ObservableHolder { Labels = source });

        ReferenceEquals(source, proxy.Labels).ShouldBeTrue();
    }

    [Test]
    public void RecursiveNesting()
    {
        var notified = 0;
        var model = new ObservableNode
        {
            Value = 1,
            Next = new ObservableNode { Value = 2 },
        };
        var proxy = new ObservableNode.Observable(model, () => notified++);

        proxy.Next!.Value = 20;
        model.Next!.Value.ShouldBe(20);
        notified.ShouldBe(1);
    }

    [Test]
    public void GeneratedNameCollisions()
    {
        var model = new ObservableCollision
        {
            Observable = "o",
            Model = "m",
            PropertyChanged = "p",
        };
        var proxy = new ObservableCollision.SparseObservable(model);
        var names = Events(proxy);

        proxy.Observable.ShouldBe("o");
        proxy.Model.ShouldBe("m");
        proxy.Observable = "o2";
        model.Observable.ShouldBe("o2");
        names.ShouldBe(["Observable"]);
    }

    [Test]
    public void NullableMembers()
    {
        var model = new ObservableHolder { Note = "x" };
        var proxy = new ObservableHolder.Observable(model);
        var names = Events(proxy);

        proxy.Note = null;
        model.Note.ShouldBeNull();
        names.ShouldBe(["Note"]);
    }
}
