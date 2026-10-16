using System.Text.Json;
using SparseTypes;

// Compile-checked tutorial sample for the end-to-end change workflow:
// EditSession -> ChangeSet -> ChangePayload -> Rebase -> Fragment/Patch.
// Run() verifies each documented transition value.
public static class TutorialFirstEditSamples
{
    public static void Run()
    {
        FirstChangeWorkflow();
        HistoryFlow();
    }

    private static void FirstChangeWorkflow()
    {
        // sample: tutorial-editsession
        var settings = new UserSettings
        {
            DisplayName = "Alice",
            Theme = "light",
            RefreshInterval = 60,
        };

        // 1. Manage draft state with an edit session
        var session = settings.CreateEditSession();

        session.Observable.Theme = "dark";
        // session.HasChanges == true
        // /sample
        DocsCheck.Require(session.HasChanges, "session tracks changes");
        DocsCheck.Require(session.Observable.Theme == "dark", "observable reflects edit");

        // sample: tutorial-changeset
        // 2. Inspect what changed
        var changes = session.CreateChangeSet();

        // changes.Theme.IsChanged == true
        // changes.Theme.Before.Value == "light"
        // changes.Theme.After.Value == "dark"
        // changes.RefreshInterval.IsChanged == false
        // /sample
        DocsCheck.Require(changes.Theme.IsChanged, "Theme changed");
        DocsCheck.Require(changes.Theme.Before.Value == "light", "Before preserves old value");
        DocsCheck.Require(changes.Theme.After.Value == "dark", "After preserves new value");
        DocsCheck.Require(!changes.RefreshInterval.IsChanged, "RefreshInterval untouched");

        // sample: tutorial-payload
        // 3. Serialize to wire transport payload
        var payload = changes.ToPayload();
        var json = JsonSerializer.Serialize(payload);

        // Deserialize on the server into a typed ChangeSet
        var receivedPayload = JsonSerializer.Deserialize<UserSettings.ChangePayload>(json)!;
        var incomingChanges = receivedPayload.ToChangeSet();
        // /sample
        DocsCheck.Require(incomingChanges.Theme.IsChanged, "incoming changes preserve Theme edit");
        DocsCheck.Require(
            incomingChanges.Theme.After.Value == "dark",
            "incoming changes preserve Theme value"
        );

        // sample: tutorial-rebase
        // 4. Reconcile concurrent changes
        // Suppose another user updated DisplayName on the server in the meantime
        var serverCurrent = new UserSettings
        {
            DisplayName = "Alice Cooper",
            Theme = "light",
            RefreshInterval = 60,
        };

        var rebaseResult = incomingChanges.RebaseOnto(serverCurrent);
        // rebaseResult.HasConflicts == false
        // rebaseResult.Rebased.Theme.After.Value == "dark"
        // /sample
        DocsCheck.Require(!rebaseResult.HasConflicts, "rebase has no conflicts");
        DocsCheck.Require(
            rebaseResult.Rebased.Theme.After.Value == "dark",
            "rebased keeps client edit"
        );

        // sample: tutorial-fragment-patch
        // 6. Low-level building blocks: Fragment layering and Patch commands
        var defaultLayer = new UserSettings.Fragment { Theme = "light", RefreshInterval = 30 };
        var userOverride = new UserSettings.Fragment { Theme = "dark" };

        var effective = defaultLayer.Merge(userOverride);
        // effective.Theme.Value == "dark"
        // effective.RefreshInterval.Value == 30

        var patch = new UserSettings.Patch { RefreshInterval = 120 };
        var updated = effective.Apply(patch);
        // updated.RefreshInterval.Value == 120
        // /sample
        DocsCheck.Require(effective.Theme.Value == "dark", "override wins");
        DocsCheck.Require(effective.RefreshInterval.Value == 30, "missing falls through");
        DocsCheck.Require(updated.RefreshInterval.Value == 120, "patch updates RefreshInterval");
    }

    private static void HistoryFlow()
    {
        var probe = new UserSettings { Theme = "light" }.CreateEditSession();
        DocsCheck.Require(probe.History is null, "history off by default");

        // sample: tutorial-history
        var draft = new UserSettings
        {
            DisplayName = "Alice",
            Theme = "light",
            RefreshInterval = 60,
        };
        var draftSession = draft.CreateEditSession();

        // History is off by default: draftSession.History == null.
        var history = draftSession.EnableHistory();

        draftSession.Observable.Theme = "dark";
        // history.CanUndo == true
        DocsCheck.Require(history.CanUndo, "edit retained in history");
        history.Undo();
        // draft.Theme == "light"
        DocsCheck.Require(draft.Theme == "light", "undo restores value");
        history.Redo();
        // draft.Theme == "dark"
        // /sample
        DocsCheck.Require(draft.Theme == "dark", "redo reapplies edit");
        draftSession.DisableHistory();
        DocsCheck.Require(draftSession.History is null, "disable releases history");
    }
}

// sample: tutorial-models
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }

    public string? Theme { get; set; }

    public int RefreshInterval { get; set; }
}

// /sample
