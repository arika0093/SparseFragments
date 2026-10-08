using System.Text.Json;
using SparseFragments;

// Canonical compile-checked mirror of the root README.md.
// Each block below corresponds to the Quick Start and the core Generated API
// sections (model shape, Optional states, Fragment merge, typed Patch,
// ChangeSet transitions with typed observation, JSON round-trip and rebase).
// Keep the model shapes (Settings with [SparseFragmentModel], reachable
// partial DatabaseSettings) in sync with the README so CI fails when the
// public generated API drifts from the documented samples.

// Presence Tracking: the three states of Optional<T>.
Optional<string?> missing = Optional<string?>.Missing;
Optional<string?> value = "hello";
Optional<string?> explicitNull = Optional<string?>.Present(null);
Require(!missing.IsPresent, "Optional missing");
Require(value.IsPresent && value.Value == "hello", "Optional present value");
Require(explicitNull.IsPresent && explicitNull.Value is null, "Optional present null");

// Quick Start: defaults, environment overrides and merge.
var defaults = Settings.Fragment.From(
    new Settings
    {
        Label = "default",
        Database = new() { Host = "db.local", Port = 5432 },
    });
var environment = new Settings.Fragment
{
    Database = new DatabaseSettings.Fragment { Port = 6432 },
};
var effective = defaults.Merge(environment);
Require(effective.Database.Value!.Port.Value == 6432, "merge higher priority wins");
Require(effective.Database.Value.Host.Value == "db.local", "merge falls through nested missing members");
Require(effective.Label.Value == "default", "merge keeps lower value for missing members");

// Quick Start: typed Patch editing (source fragment is never mutated).
var patch = new Settings.Patch { Label = "production" };
patch.Database.Port = 7432;
var updated = effective.Apply(patch);
Require(updated.ToModel().Label == "production", "typed patch applies");
Require(updated.Database.Value!.Port.Value == 7432, "typed nested set");

// Quick Start: ChangeSet captures the before -> after transition.
var changes = Settings.ChangeSet.Between(effective, updated);
Require(changes.Label.IsChanged, "typed transition reports the Label change");
Require(changes.Label.Before.Value == "default", "typed transition Before");
Require(changes.Label.After.Value == "production", "typed transition After");
Require(changes.Database.Port.IsChanged, "typed nested transition reports the Port change");
Require(changes.Database.Port.Before.Value == 6432, "typed nested transition Before");
Require(changes.Database.Port.After.Value == 7432, "typed nested transition After");
var replayed = effective.Apply(changes.ToPatch());
Require(replayed.ToModel().Label == "production", "ChangeSet.ToPatch replays the transition");

// Client/Server Edits: the typed payload crosses process boundaries as ordinary System.Text.Json.
var json = JsonSerializer.Serialize(changes.ToPayload());
var incoming = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!.ToChangeSet();
Require(incoming.Label.IsChanged, "ChangeSet payload JSON round-trip preserves transitions");
var rebased = incoming.RebaseOnto(updated);
Require(!rebased.HasConflicts, "rebase onto current has no conflicts");

Console.WriteLine("SparseFragments README consumer passed.");

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
[SparseFragmentModel]
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
