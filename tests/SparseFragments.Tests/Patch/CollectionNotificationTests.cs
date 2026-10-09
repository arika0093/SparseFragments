using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using SparseFragments.Generated;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class CollectionNotifyModel
{
    public List<string> Tags { get; set; } = [];

    public Dictionary<string, string> Map { get; set; } = [];

    [SparseCloneReferenceSafe]
    public ObservableCollection<string> Live { get; set; } = [];
}

/// <summary>Held collection views stay notified across in-place writes (#168).</summary>
public sealed class CollectionNotificationTests
{
    [Test]
    public void ApplyInPlaceResetsHeldListAndDictionaryViews()
    {
        var model = new CollectionNotifyModel
        {
            Tags = ["a"],
            Map = new() { ["key"] = "value" },
        };
        var tags = model.Tags;
        var map = model.Map;
        var session = model.CreateEditSession();
        var tagsView = session.Observable.Tags;
        var mapView = session.Observable.Map;
        var tagActions = new List<NotifyCollectionChangedAction>();
        var mapActions = new List<NotifyCollectionChangedAction>();
        var topProperties = new List<string?>();
        tagsView.CollectionChanged += (_, args) => tagActions.Add(args.Action);
        mapView.CollectionChanged += (_, args) => mapActions.Add(args.Action);
        session.Observable.PropertyChanged += (_, args) => topProperties.Add(args.PropertyName);

        var edited = new CollectionNotifyModel
        {
            Tags = ["b", "c"],
            Map = new() { ["key"] = "updated" },
            // Untouched reference-safe members must share the instance; distinct but
            // equal instances compare by reference and would stay pending forever.
            Live = model.Live,
        };
        session.ApplyInPlace(model.CreateChangeSet(edited));

        tagActions.ShouldBe([NotifyCollectionChangedAction.Reset]);
        mapActions.ShouldBe([NotifyCollectionChangedAction.Reset]);
        topProperties.ShouldContain(nameof(CollectionNotifyModel.Tags));
        topProperties.ShouldContain(nameof(CollectionNotifyModel.Map));
        ReferenceEquals(model.Tags, tags).ShouldBeTrue();
        ReferenceEquals(model.Map, map).ShouldBeTrue();
        model.Tags.ShouldBe(["b", "c"]);
        model.Map.ShouldBe(new Dictionary<string, string> { ["key"] = "updated" });

        // Applying does not advance the baseline: the applied edit stays pending.
        var pending = session.CreateChangeSet();
        pending.Tags.After.Value.ShouldBe(["b", "c"]);
        pending.Map.Edited["key"].ShouldBe("updated");
        session.AcceptChanges();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void RevertChangesResetsHeldViews()
    {
        var model = new CollectionNotifyModel
        {
            Tags = ["a"],
            Map = new() { ["key"] = "value" },
        };
        var session = model.CreateEditSession();
        var tagsView = session.Observable.Tags;
        var mapView = session.Observable.Map;
        var tagActions = new List<NotifyCollectionChangedAction>();
        var mapActions = new List<NotifyCollectionChangedAction>();
        tagsView.CollectionChanged += (_, args) => tagActions.Add(args.Action);
        mapView.CollectionChanged += (_, args) => mapActions.Add(args.Action);

        session.Observable.Tags.Add("transient");
        session.Observable.Map.Add("extra", "transient");
        tagActions.Clear();
        mapActions.Clear();

        session.RevertChanges();

        tagActions.ShouldBe([NotifyCollectionChangedAction.Reset]);
        mapActions.ShouldBe([NotifyCollectionChangedAction.Reset]);
        model.Tags.ShouldBe(["a"]);
        model.Map.ShouldBe(new Dictionary<string, string> { ["key"] = "value" });
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReloadResetsHeldViews()
    {
        var model = new CollectionNotifyModel { Tags = ["a"] };
        var session = model.CreateEditSession();
        var tagsView = session.Observable.Tags;
        var tagActions = new List<NotifyCollectionChangedAction>();
        tagsView.CollectionChanged += (_, args) => tagActions.Add(args.Action);

        session
            .Reload(new CollectionNotifyModel { Tags = ["server"] })
            .HasConflicts.ShouldBeFalse();

        tagActions.ShouldBe([NotifyCollectionChangedAction.Reset]);
        model.Tags.ShouldBe(["server"]);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReplacedNotifyingCollectionsRetireOldViewsWithoutDuplicateResets()
    {
        var model = new CollectionNotifyModel { Live = ["a"] };
        var session = model.CreateEditSession();
        var liveView = session.Observable.Live;
        var liveActions = new List<NotifyCollectionChangedAction>();
        var topProperties = new List<string?>();
        liveView.CollectionChanged += (_, args) => liveActions.Add(args.Action);
        session.Observable.PropertyChanged += (_, args) => topProperties.Add(args.PropertyName);

        session.Reload(new CollectionNotifyModel { Live = ["b"] }).HasConflicts.ShouldBeFalse();

        // The backing instance was replaced rather than bulk-mutated: bindings are
        // notified through the property, and the retired view reports no Reset.
        liveActions.ShouldBeEmpty();
        topProperties.ShouldContain(nameof(CollectionNotifyModel.Live));
        liveView.IsDisposed.ShouldBeTrue();
        session.Observable.Live.ShouldBe(["b"]);
        session.HasChanges.ShouldBeFalse();
    }
}
