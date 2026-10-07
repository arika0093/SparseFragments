using System.Text.Json;
using SparseFragments;

// Canonical compile-checked mirror of the root README.md.
// Each block below corresponds to a README sample. Keep the model shapes
// (Settings with [SparseFragmentModel], reachable partial Child) in sync with
// the README so CI fails when the public generated API drifts from the
// documented samples.

// Quick start: the three states of Optional<T>.
Optional<string?> missing = Optional<string?>.Missing;
Optional<string?> presentNull = Optional<string?>.Present(null);
Optional<string?> presentValue = "hello";
Require(!missing.IsPresent, "Optional missing");
Require(presentNull.IsPresent && presentNull.Value is null, "Optional present null");
Require(presentValue.IsPresent && presentValue.Value == "hello", "Optional present value");

// Quick start: Fragment layering (missing falls through, present wins).
var defaults = Settings.Fragment.From(
    new Settings
    {
        Label = "fallback",
        Child = new Child { Host = "db.local" },
    });
var overlay = new Settings.Fragment
{
    Child = new Child.Fragment { Count = 9 },
};
var effective = defaults.Merge(overlay).ToModel();
Require(effective.Label == "fallback", "merge keeps lower value for missing members");
Require(effective.Child!.Host == "db.local", "merge falls through nested missing members");
Require(effective.Child.Count == 9, "merge higher priority wins");

// Quick start: Patch desired operations (source fragment is never mutated).
var patch = new Settings.Patch { Label = (string?)null };
patch.Child.Count = 9;
var updated = defaults.Apply(patch);
Require(updated.Label.IsPresent && updated.Label.Value is null, "typed patch present null");
Require(updated.Child.Value!.Count.Value == 9, "typed nested set");
Require(updated.Child.Value.Host.Value == "db.local", "typed patch keeps unspecified members");
Require(defaults.Child.Value!.Count.Value == 0, "original fragment isolation");

// Quick start: ChangeSet captures the immutable before -> after transition.
var changes = Settings.ChangeSet.Between(
    Optional<Settings.Fragment?>.Present(defaults),
    Optional<Settings.Fragment?>.Present(updated));
Require(!changes.IsEmpty, "ChangeSet.Between detects the transition");
var replayed = defaults.Apply(changes.ToPatch());
Require(replayed.Label.IsPresent && replayed.Label.Value is null, "ChangeSet.ToPatch replays the transition");
Require(replayed.Child.Value!.Count.Value == 9, "ChangeSet.ToPatch replays nested members");

// Workflows: minimal persisted settings via Fragment.Diff and ApplyChanges.
var delta = Settings.Fragment.Diff(new Settings(), new Settings { Label = "custom" });
var restored = Settings.Fragment.From(new Settings()).ApplyChanges(delta);
Require(restored.Label.Value == "custom", "Diff/ApplyChanges persist only what differs");

// Workflows: local desired edits compose baseline-free.
var first = new Settings.Patch { Label = "a" };
var second = new Settings.Patch();
second.Child.Count = 2;
var combined = first.Compose(second);
var composedApplied = defaults.Apply(combined);
Require(composedApplied.Label.Value == "a", "Patch.Compose keeps first operations");
Require(composedApplied.Child.Value!.Count.Value == 2, "Patch.Compose keeps second operations");

var clear = new Settings.Patch();
clear.Child.SetNull();
var nulled = defaults.Apply(clear);
Require(nulled.Child.IsPresent && nulled.Child.Value is null, "typed nested SetNull");
var drop = new Settings.Patch();
drop.Child.Unset();
Require(!defaults.Apply(drop).Child.IsPresent, "typed nested Unset");

// Workflows: observed transitions invert without an external baseline.
var transition = Settings.ChangeSet.Between(
    Optional<Settings.Fragment?>.Present(defaults),
    Optional<Settings.Fragment?>.Present(updated));
var undone = transition.Invert();
var walkedBack = undone.ToPatch().Apply(Optional<Settings.Fragment?>.Present(updated));
Require(walkedBack.Value!.Label.Value == "fallback", "ChangeSet.Invert walks back");

// Workflows: concurrent reconciliation via RebaseOnto.
var current = Optional<Settings.Fragment?>.Present(
    Settings.Fragment.From(new Settings { Label = "concurrent" }));
var rebased = transition.RebaseOnto(current);
Require(rebased.HasConflicts, "RebaseOnto reports the Label conflict");
Require(rebased.Conflicts.Any(c => c.Path.SequenceEqual(["Label"])), "RebaseOnto conflict path");

var cleanCurrent = Optional<Settings.Fragment?>.Present(
    Settings.Fragment.From(new Settings { Label = "fallback", Child = new Child { Host = "other" } }));
var cleanRebased = transition.RebaseOnto(cleanCurrent);
Require(!cleanRebased.HasConflicts, "RebaseOnto replays disjoint members");
var saved = cleanRebased.Patch.ToPatch().Apply(cleanCurrent);
Require(saved.Value!.Label.Value is null, "rebased ChangeSet replays the transition");

// End to end: local state -> shared change as ordinary System.Text.Json.
var state = Settings.Fragment.From(new Settings { Label = "v1" });
var edit = new Settings.Patch { Label = "v2" };
var edited = state.Apply(edit);
var outgoing = Settings.ChangeSet.Between(
    Optional<Settings.Fragment?>.Present(state),
    Optional<Settings.Fragment?>.Present(edited));
var json = JsonSerializer.Serialize(outgoing);
var incoming = JsonSerializer.Deserialize<Settings.ChangeSet>(json)!;
var arrival = incoming.RebaseOnto(Optional<Settings.Fragment?>.Present(state));
var arrivalSaved = arrival.Patch.ToPatch().Apply(Optional<Settings.Fragment?>.Present(state));
Require(arrivalSaved.Value!.Label.Value == "v2", "ChangeSet JSON round-trip and rebase");
Require(json.Contains("v2"), "ChangeSet JSON export value");

Console.WriteLine("SparseFragments README consumer passed.");

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

// Mirrors the README quick-start model. Reachable partial nested models
// auto-generate Fragment/Patch, so Child stays undecorated but partial because
// the samples construct Child.Fragment directly.
[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public Child? Child { get; set; }
}

public partial class Child
{
    public int Count { get; set; }
    public string Host { get; set; } = "localhost";
}
