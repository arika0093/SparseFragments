using SparseTypes;
using SparseTypes.Generated;

public static class EditSessionStateDocsSamples
{
    public static void Run()
    {
        FieldStateQuery();
        CollectionStateQuery();
        ObserveFieldLifecycle();
    }

    private static void FieldStateQuery()
    {
        // sample: edit-session-field-state
        var widget = new UiWidget
        {
            Title = "original",
            Child = new UiWidgetChild { Name = "child-original" },
        };
        var session = widget.CreateEditSession();

        // Mutate child name
        session.Observable.Child!.Name = "child-updated";

        // Query field state at exact path
        var childNameState = session.FieldState(session.Paths.Child.Name);
        // childNameState.IsChanged == true
        // childNameState.Kind == SparseFieldChangeKind.Changed
        // childNameState.HasAncestorChange == false
        // childNameState.HasDescendantChange == false
        // childNameState.HasChanges == true
        // childNameState.Before.Value == "child-original"
        // childNameState.After.Value == "child-updated"

        // Query field state at ancestor path (Child)
        var childState = session.FieldState(session.Paths.Child);
        // childState.IsChanged == false (Child reference itself was not replaced)
        // childState.HasDescendantChange == true
        // childState.HasChanges == true

        // For repeated queries, snapshot a single ChangeSet to avoid re-diffing:
        var changeSet = session.CreateChangeSet();
        var fromSnapshot = changeSet.FieldState(session.Paths.Child.Name);
        // fromSnapshot.IsChanged == true
        // /sample

        DocsCheck.Require(childNameState.IsChanged, "exact path reports change");
        DocsCheck.Require(childState.HasDescendantChange, "ancestor reports descendant change");
        DocsCheck.Require(!childState.IsChanged, "ancestor itself is unchanged");
        DocsCheck.Require(fromSnapshot.IsChanged, "snapshot query matches live query");
    }

    private static void CollectionStateQuery()
    {
        // sample: edit-session-collection-state
        var widget = new UiWidget
        {
            Items =
            [
                new UiWidgetItem { Id = "a", Name = "item-a" },
                new UiWidgetItem { Id = "b", Name = "item-b" },
            ],
        };
        var session = widget.CreateEditSession();

        // Reorder and edit
        session.Observable.Items.Move(1, 0);
        session.Observable.Items[0].Name = "item-b-edited";

        // Query collection state for Items
        var collectionState = session.CollectionState(session.Paths.Items);
        var itemBState = collectionState.GetItemState("b");
        // itemBState.IsEdited == true
        // itemBState.IsReordered == true
        // itemBState.IsAdded == false
        // itemBState.IsRemoved == false

        // Unknown key yields empty state
        var unknownState = collectionState.GetItemState("nonexistent");
        // unknownState.IsEmpty == true
        // /sample

        DocsCheck.Require(itemBState.IsEdited, "item reports edit");
        DocsCheck.Require(itemBState.IsReordered, "item reports reorder");
        DocsCheck.Require(unknownState.IsEmpty, "unknown key yields empty state");
    }

    private static void ObserveFieldLifecycle()
    {
        // sample: edit-session-observe-field
        var widget = new UiWidget { Title = "start" };
        var session = widget.CreateEditSession();

        var changeCount = 0;
        using var watcher = session.ObserveField(session.Paths.Title);
        watcher.PropertyChanged += (_, _) => changeCount++;

        // Observable change notifies watcher synchronously
        session.Observable.Title = "next";
        // changeCount > 0
        // watcher.Current.Value == "next"
        // watcher.IsChanged == true

        var lastCount = changeCount;
        // Setting same value raises nothing (effective change suppression)
        session.Observable.Title = "next";
        // changeCount == lastCount

        // Disposing detaches notifications
        watcher.Dispose();
        session.Observable.Title = "after-dispose";
        // changeCount == lastCount
        // /sample

        DocsCheck.Require(changeCount == lastCount, "watcher detached after dispose");
        DocsCheck.Require(session.Current.Title == "after-dispose", "model updated after dispose");
    }
}
