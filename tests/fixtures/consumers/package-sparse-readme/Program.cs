using System.Text.Json;
using SparseTypes;

// Canonical compile-checked mirror of the root README.md.
// The `// sample: <id>` regions below match the same-id fenced blocks in
// README.md exactly after normalization; verify-sparsetypes-readme.sh
// fails on drift. Regions execute as top-level statements so documented
// results are verified, not just compiled. Keep the model shapes (Settings
// with [SparseModel], reachable partial DatabaseSettings) in sync
// with the README so CI fails when the public generated API drifts from the
// documented samples.

// Quick Start: reactive editing through EditSession, inspecting changes, and reverting.
// sample: readme-quickstart
var settings = new Settings
{
    Label = "default",
    Database = new() { Host = "db.local", Port = 5432 },
};

// 1. Start a reactive edit session
var session = settings.CreateEditSession();

// 2. Edit through the observable proxy
session.Observable.Label = "production";
session.Observable.Database!.Port = 7432;

// 3. Inspect dirty state and typed transitions
var changes = session.CreateChangeSet();

Console.WriteLine(session.HasChanges); // True
Console.WriteLine(changes.Label.IsChanged); // True
Console.WriteLine(changes.Database.Port.Before.Value); // 5432
Console.WriteLine(changes.Database.Port.After.Value); // 7432

// 4. Revert edits when needed
session.RevertChanges();
Console.WriteLine(session.HasChanges); // False
Console.WriteLine(settings.Label); // default

// /sample
Require(!session.HasChanges, "session reverted has no changes");
Require(settings.Label == "default", "revert restored Label");

// Re-apply edit for wire transport demonstration
session.Observable.Label = "production";
session.Observable.Database!.Port = 7432;
var outgoing = session.CreateChangeSet();

// Client/Server Edits: the typed payload crosses process boundaries as ordinary System.Text.Json.
var json = JsonSerializer.Serialize(outgoing.ToPayload());
var incoming = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!.ToChangeSet();
Require(incoming.Label.IsChanged, "ChangeSet payload JSON round-trip preserves transitions");
var updated = Settings.Fragment.From(settings).Apply(incoming.ToPatch());
var rebased = incoming.RebaseOnto(updated);
Require(!rebased.HasConflicts, "rebase onto current has no conflicts");

Console.WriteLine("SparseTypes README consumer passed.");

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

// Mirrors the README Quick Start model. Reachable partial nested models
// auto-generate Fragment/Patch, so DatabaseSettings stays undecorated but
// partial because the samples construct DatabaseSettings.Fragment directly.
// sample: readme-quickstart
[SparseModel]
public partial class Settings
{
    public string? Label { get; set; }
    public DatabaseSettings? Database { get; set; }
}

public partial class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
}
// /sample
