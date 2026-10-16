using System.Text.Json;
using SparseTypes;

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

// sample: tutorial-program
// 1. Manage draft state
var settings = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};
var session = settings.CreateEditSession();
var history = session.EnableHistory();
Require(ReferenceEquals(session.History, history), "history is enabled on the session");

session.Observable.Theme = "dark";
// history.CanUndo == true
Require(history.CanUndo, "history retains the edit");
history.Undo();
// settings.Theme == "light"
Require(settings.Theme == "light", "undo restores the original value");
history.Redo();
// settings.Theme == "dark"
Require(settings.Theme == "dark", "redo reapplies the edit");

// 2. Inspect what changed
var changes = session.CreateChangeSet();
// changes.Theme.IsChanged == true
// changes.Theme.Before.Value == "light"
// changes.Theme.After.Value == "dark"
Require(changes.Theme.IsChanged, "ChangeSet detects the Theme edit");
Require(changes.Theme.Before.Value == "light", "ChangeSet retains the old value");
Require(changes.Theme.After.Value == "dark", "ChangeSet retains the new value");

// 3. Wire transport payload
var payload = changes.ToPayload();
var json = JsonSerializer.Serialize(payload);
var receivedPayload = JsonSerializer.Deserialize<UserSettings.ChangePayload>(json)!;
var incomingChanges = receivedPayload.ToChangeSet();
// incomingChanges.Theme.IsChanged == true
// incomingChanges.Theme.After.Value == "dark"
Require(incomingChanges.Theme.IsChanged, "JSON round-trip retains the changed property");
Require(incomingChanges.Theme.Before.Value == "light", "JSON round-trip retains the old value");
Require(incomingChanges.Theme.After.Value == "dark", "JSON round-trip retains the new value");

// 4. Reconcile concurrent changes
var serverCurrent = new UserSettings
{
    DisplayName = "Alice Cooper",
    Theme = "light",
    RefreshInterval = 60,
};
var rebaseResult = incomingChanges.RebaseOnto(serverCurrent);
// rebaseResult.HasConflicts == false
// rebaseResult.Rebased.Theme.After.Value == "dark"
Require(!rebaseResult.HasConflicts, "independent server and client edits do not conflict");
Require(
    rebaseResult.Rebased.Theme.After.Value == "dark",
    "rebase retains the client Theme edit"
);

// 6. Low-level Fragment and Patch
var defaultLayer = new UserSettings.Fragment { Theme = "light", RefreshInterval = 30 };
var userOverride = new UserSettings.Fragment { Theme = "dark" };
var effective = defaultLayer.Merge(userOverride);
var updated = effective.Apply(new UserSettings.Patch { RefreshInterval = 120 });
// effective.Theme.Value == "dark"
// effective.RefreshInterval.Value == 30
// updated.RefreshInterval.Value == 120
Require(effective.Theme.Value == "dark", "override replaces the default Theme");
Require(effective.RefreshInterval.Value == 30, "missing override preserves the default");
Require(updated.RefreshInterval.Value == 120, "patch updates RefreshInterval");

Console.WriteLine($"Rebased Theme: {rebaseResult.Rebased.Theme.After.Value}");

// Model declaration at the bottom
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public int RefreshInterval { get; set; }
}
// /sample
