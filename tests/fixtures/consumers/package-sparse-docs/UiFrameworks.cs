using SparseFragments;
using SparseFragments.Generated;

// Canonical compile-checked mirror of docs/ui-frameworks.md (#48).
// Covers the shared UI editing model: bind the session's generated Observable
// proxy, keep editing the underlying live model, and derive patches from the
// session baseline.
public static class UiFrameworksSamples
{
    public static void Run()
    {
        ObservableBinding();
        ObservableCollectionBinding();
        PatchFromBaselineVersusCurrent();
        CurrentReflectsLiveObservableEdits();
        BatchEditGroupsNotificationsAndReverts();
        AdvancedInspectionUsesDescriptorsAndFlattenedChanges();
        AcceptSubmittedAdvancesBaselineOnly();
        ReloadKeepsPendingEdits();
        ReloadConflictLeavesSessionUntouched();
        RevertRestoresBaseline();
        DescriptorFirst();
        DescriptorChanges();
    }

    private static void ObservableCollectionBinding()
    {
        var model = new UiWidget { Items = [new UiWidgetItem { Id = "a", Name = "first" }] };
        var collectionChanges = 0;
        var session = model.CreateEditSession();
        var observable = session.Observable;
        observable.Items.CollectionChanged += (_, _) => collectionChanges++;

        observable.Items.AddModel(new UiWidgetItem { Id = "b", Name = "second" });
        observable.Items[0].Name = "updated";

        DocsCheck.Require(model.Items.Count == 2, "view mutations update the model list");
        DocsCheck.Require(collectionChanges == 1, "list edits raise collection notifications");
        DocsCheck.Require(
            session.CreateChangeSet().Items.IsChanged,
            "element proxy edits are reflected in the session change set"
        );
    }

    private static void ObservableBinding()
    {
        var model = new UiWidget
        {
            Title = "a",
            Child = new UiWidgetChild { Name = "n" },
        };
        var seen = new List<string>();
        var session = model.CreateEditSession();
        var observable = session.Observable;
        observable.PropertyChanged += (_, args) => seen.Add(args.PropertyName ?? "<null>");

        observable.Title = "b";
        DocsCheck.Require(model.Title == "b", "observable writes through to the model");
        DocsCheck.Require(
            seen.Count == 1 && seen[0] == "Title",
            "scalar set raises PropertyChanged"
        );

        observable.Title = "b";
        DocsCheck.Require(seen.Count == 1, "same value does not notify");

        observable.Child!.Name = "n2";
        DocsCheck.Require(model.Child!.Name == "n2", "nested proxy edits the model");
    }

    private static void PatchFromBaselineVersusCurrent()
    {
        var model = new UiWidget { Title = "a" };
        var session = model.CreateEditSession();

        model.Title = "b";
        var changes = session.CreateChangeSet();
        DocsCheck.Require(!changes.IsEmpty, "baseline/current diff is the patch source");

        model.Title = "a";
        var restored = session.CreateChangeSet();
        DocsCheck.Require(restored.IsEmpty, "edit-then-restore is empty");
    }

    private static void CurrentReflectsLiveObservableEdits()
    {
        var model = new UiWidget { Title = "a" };
        var session = model.CreateEditSession();

        session.Observable.Title = "b";
        DocsCheck.Require(session.Current.Title == "b", "read-only view reflects live edits");
        DocsCheck.Require(model.Title == "b", "observable writes through to the model");
    }

    private static void BatchEditGroupsNotificationsAndReverts()
    {
        var model = new UiWidget { Title = "a" };
        var session = model.CreateEditSession();
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        session.BatchEdit(() =>
        {
            session.Observable.Title = "b";
            session.Observable.Title = "c";
        });
        DocsCheck.Require(transitions == 1, "batch raises one transition notification");
        DocsCheck.Require(model.Title == "c", "batched edits apply to the model");
        DocsCheck.Require(session.HasChanges, "batched edits stay pending");

        session.RevertChanges();
        DocsCheck.Require(model.Title == "a", "revert restores the baseline value");
        DocsCheck.Require(!session.HasChanges, "revert clears pending changes");
    }

    private static void AdvancedInspectionUsesDescriptorsAndFlattenedChanges()
    {
        var model = new UiWidget { Title = "a" };
        var session = model.CreateEditSession();

        session.Observable.Title = "b";
        DocsCheck.Require(
            session.Descriptors.TryGet(nameof(UiWidget.Title), out var descriptor),
            "descriptor lookup by member name"
        );
        DocsCheck.Require(descriptor.GetValue() is "b", "descriptor reads the live value");

        var changes = session.CreateChangeSet();
        var flattened = changes.EnumerateChanges().ToList();
        DocsCheck.Require(
            flattened.Count == 1 && flattened[0].Path == nameof(UiWidget.Title),
            "flattened enumeration names the changed path"
        );
        DocsCheck.Require(
            session.EnumerateChangedPaths().SequenceEqual(new[] { nameof(UiWidget.Title) }),
            "changed paths list the member"
        );
    }

    private static void AcceptSubmittedAdvancesBaselineOnly()
    {
        // sample: ui-accept-flow
        var orderModel = new UiOrder { Number = "a" };
        var orderSession = orderModel.CreateEditSession();

        orderSession.Observable.Number = "b";
        var submitted = orderSession.CreateChangeSet();

        // The server persisted the submitted transition unchanged,
        // and the user kept typing while the save was in flight.
        orderSession.Observable.Number = "c";
        orderSession.AcceptChanges(submitted);
        // orderSession.HasChanges == true
        // orderSession.CreateChangeSet() carries only Number "b" -> "c"

        // When the server returns the authoritative state instead, start over from it.
        var persisted = new UiOrder { Number = "b" };
        var freshSession = persisted.CreateEditSession();
        // freshSession.HasChanges == false
        // /sample
        DocsCheck.Require(orderSession.HasChanges, "later edits stay pending");
        var pending = orderSession.CreateChangeSet();
        DocsCheck.Require(
            pending.Number.Before.Value == "b" && pending.Number.After.Value == "c",
            "baseline advanced by the submitted transition"
        );
        DocsCheck.Require(!freshSession.HasChanges, "fresh session on persisted state is clean");
    }

    private static void ReloadKeepsPendingEdits()
    {
        // sample: ui-reload
        var reloadModel = new UiOrder { Number = "base" };
        var reloadSession = reloadModel.CreateEditSession();
        reloadSession.Observable.Number = "local";
        var serverState = new UiOrder
        {
            Number = "base",
            Items = [new UiOrderItem { Id = "line-1", Name = "First item" }],
        };

        var reload = reloadSession.Reload(serverState);
        // reload.HasConflicts == false
        // reloadSession.Model.Number == "local"
        // reloadSession.Model.Items.Count == 1
        // reloadSession.HasChanges == true
        // /sample
        DocsCheck.Require(!reload.HasConflicts, "disjoint server state merges cleanly");
        DocsCheck.Require(
            ReferenceEquals(reloadSession.Model, reloadModel),
            "reload keeps the live model instance"
        );
        DocsCheck.Require(
            reloadSession.Model.Number == "local" && reloadSession.Model.Items.Count == 1,
            "pending edit kept and server state adopted"
        );
        var pending = reloadSession.CreateChangeSet();
        DocsCheck.Require(
            pending.Number.Before.Value == "base" && pending.Number.After.Value == "local",
            "server state becomes the new baseline"
        );
    }

    private static void ReloadConflictLeavesSessionUntouched()
    {
        // sample: ui-reload-conflict
        var conflictModel = new UiOrder { Number = "base" };
        var conflictSession = conflictModel.CreateEditSession();
        conflictSession.Observable.Number = "local";
        var conflictingServer = new UiOrder { Number = "server" };

        var conflicted = conflictSession.Reload(conflictingServer);
        // conflicted.HasConflicts == true
        // conflictSession.Model.Number == "local"
        // conflictSession.HasChanges == true
        // /sample
        DocsCheck.Require(conflicted.HasConflicts, "overlapping edit reports a conflict");
        DocsCheck.Require(
            conflicted.Conflicts.Single().PathText == nameof(UiOrder.Number),
            "conflict names the member"
        );
        DocsCheck.Require(
            conflictSession.Model.Number == "local" && conflictSession.HasChanges,
            "conflicted reload leaves the live model and baseline untouched"
        );
    }

    private static void RevertRestoresBaseline()
    {
        // sample: ui-revert
        var revertModel = new UiOrder { Number = "a" };
        var revertSession = revertModel.CreateEditSession();
        revertSession.Observable.Number = "b";

        var reverted = revertSession.TryRevertChanges(out var revertConflicts);
        // reverted == true
        // revertConflicts is null
        // revertModel.Number == "a"
        // revertSession.HasChanges == false
        // /sample
        DocsCheck.Require(reverted, "mutable edits revert in place");
        DocsCheck.Require(revertConflicts is null, "clean revert reports no conflicts");
        DocsCheck.Require(
            revertModel.Number == "a" && !revertSession.HasChanges,
            "revert restores the baseline value"
        );
    }

    private static void DescriptorFirst()
    {
        // sample: ui-descriptor-first
        var catalogModel = new UiOrder { Number = "a" };
        var catalogSession = catalogModel.CreateEditSession();

        var found = catalogSession.Descriptors.TryGet("Number", out var title);
        // found == true
        // title.Name == "Number"
        // title.GetValue() is "a"
        var metadata = (ISparsePropertyMetadata)title;
        // metadata.DeclaredType == typeof(string)
        // metadata.IsNullable == false

        var renamed = title.TrySetValue("b");
        // renamed == true
        // catalogModel.Number == "b"

        var items = catalogSession.Descriptors.TryGet("Items", out var itemsDescriptor);
        // items == true
        var added = itemsDescriptor.Array!.TryAdd(new UiOrderItem { Id = "a", Name = "first" });
        // added == true
        // catalogModel.Items.Count == 1
        // /sample
        DocsCheck.Require(found && title.Name == "Number", "descriptor lookup by member name");
        DocsCheck.Require(title.GetValue() is "b", "descriptor reads the live value");
        DocsCheck.Require(
            metadata.DeclaredType == typeof(string) && !metadata.IsNullable,
            "static metadata describes the declared member"
        );
        DocsCheck.Require(
            renamed && catalogModel.Number == "b",
            "descriptor writes the live model"
        );
        DocsCheck.Require(
            items && added && catalogModel.Items.Count == 1,
            "collection descriptor mutates through the observable view"
        );
    }

    private static void DescriptorChanges()
    {
        // sample: ui-descriptor-changes
        var logModel = new UiOrder { Number = "a" };
        var logSession = logModel.CreateEditSession();
        logSession.Observable.Number = "b";

        var rows = logSession.CreateChangeSet().EnumerateChanges().ToList();
        // rows.Count == 1
        // rows[0].Path == "Number"
        // rows[0].Kind reports an edited value
        // logSession.EnumerateChangedPaths() lists "Number"
        // /sample
        DocsCheck.Require(
            rows.Count == 1 && rows[0].Path == nameof(UiOrder.Number),
            "flattened enumeration names the changed path"
        );
        DocsCheck.Require(
            rows[0].Kind.ToString() == "Changed",
            "flattened enumeration classifies the edit"
        );
        DocsCheck.Require(
            logSession.EnumerateChangedPaths().SequenceEqual(new[] { nameof(UiOrder.Number) }),
            "changed paths list the member"
        );
    }
}

[SparseFragmentModel]
public partial class UiWidgetChild
{
    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class UiWidget
{
    public string Title { get; set; } = string.Empty;

    public UiWidgetChild? Child { get; set; }

    public List<UiWidgetItem> Items { get; set; } = [];
}

[SparseFragmentModel]
public partial class UiWidgetItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

// sample: ui-descriptor-models
[SparseFragmentModel]
public partial class UiOrder
{
    public string Number { get; set; } = string.Empty;

    public List<UiOrderItem> Items { get; set; } = new();
}

public partial class UiOrderItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
// /sample
