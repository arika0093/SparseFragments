using System.Text.Json;
using SparseFragments;

// Canonical compile-checked mirror of the root README.md.
// Each block below corresponds to a numbered Quick Start step. Keep the model
// shapes (Settings with [SparseFragmentModel], reachable partial Child) in sync
// with the README so CI fails when the public generated API drifts from the
// documented samples.

// Quick Start step 2: the three states of Optional<T>.
Optional<string?> missing = Optional<string?>.Missing;
Optional<string?> value = "hello";
Optional<string?> explicitNull = Optional<string?>.Present(null);
Require(!missing.IsPresent, "Optional missing");
Require(value.IsPresent && value.Value == "hello", "Optional present value");
Require(explicitNull.IsPresent && explicitNull.Value is null, "Optional present null");

// Quick Start step 2: presence is concrete on Fragment (explicit null wins, missing falls through).
var defaults = Settings.Fragment.From(
    new Settings
    {
        Label = "fallback",
        Child = new Child { Host = "db.local" },
    });
var clearsLabel = new Settings.Fragment { Label = (string?)null };
var saysNothing = new Settings.Fragment();
Require(defaults.Merge(clearsLabel).ToModel().Label is null, "explicit null overrides the lower layer");
Require(defaults.Merge(saysNothing).ToModel().Label == "fallback", "missing falls through");

// Quick Start step 3: Merge layered contributions (nested merge plus one Append rule).
var lower = Settings.Fragment.From(
    new Settings
    {
        Label = "base",
        Child = new Child { Host = "db.local" },
        Plugins = ["base-plugin"],
    });
var higher = new Settings.Fragment
{
    Child = new Child.Fragment { Count = 9 },
    Plugins = new[] { "extra-plugin" },
};
var merged = lower.Merge(higher).ToModel();
Require(merged.Label == "base", "merge keeps lower value for missing members");
Require(merged.Child!.Host == "db.local", "merge falls through nested missing members");
Require(merged.Child.Count == 9, "merge higher priority wins");
Require(merged.Plugins.SequenceEqual(["base-plugin", "extra-plugin"]), "merge Append concatenates");

// Quick Start step 4: Patch desired operations (source fragment is never mutated).
var patch = new Settings.Patch { Label = (string?)null };
patch.Child.Count = 9;
Require(new Settings.Patch().IsEmpty, "fresh patch is empty");
Require(!patch.IsEmpty, "populated patch is not empty");
var updated = defaults.Apply(patch);
Require(updated.Label.IsPresent && updated.Label.Value is null, "typed patch present null");
Require(updated.Child.Value!.Count.Value == 9, "typed nested set");
Require(updated.Child.Value.Host.Value == "db.local", "typed patch keeps unspecified members");
Require(defaults.Child.Value!.Count.Value == 0, "original fragment isolation");

var clear = new Settings.Patch();
clear.Child.SetNull();
var nulled = defaults.Apply(clear);
Require(nulled.Child.IsPresent && nulled.Child.Value is null, "typed nested SetNull");
var drop = new Settings.Patch();
drop.Child.Unset();
Require(!defaults.Apply(drop).Child.IsPresent, "typed nested Unset");

// Quick Start step 5: ChangeSet captures the immutable before -> after transition.
var before = Optional<Settings.Fragment?>.Present(defaults);
var after = Optional<Settings.Fragment?>.Present(updated);
var changes = Settings.ChangeSet.Between(before, after);
Require(!changes.IsEmpty, "ChangeSet.Between detects the transition");
Require(changes.Label.IsChanged, "typed transition reports the Label change");
var beforeLabel = changes.Label.Before;
var afterLabel = changes.Label.After;
Require(beforeLabel.Value == "fallback", "typed transition Before");
Require(afterLabel.IsPresent && afterLabel.Value is null, "typed transition After");
var replayed = defaults.Apply(changes.ToPatch());
Require(replayed.Label.IsPresent && replayed.Label.Value is null, "ChangeSet.ToPatch replays the transition");
Require(replayed.Child.Value!.Count.Value == 9, "ChangeSet.ToPatch replays nested members");

// Quick Start step 6: transitions cross process boundaries as ordinary System.Text.Json.
var json = JsonSerializer.Serialize(changes);
var restored = JsonSerializer.Deserialize<Settings.ChangeSet>(json)!;
Require(restored.Label.IsChanged, "ChangeSet JSON round-trip preserves transitions");
var restoredReplayed = restored.ToPatch().Apply(before);
Require(
    restoredReplayed.Value!.Label.IsPresent && restoredReplayed.Value.Label.Value is null,
    "ChangeSet JSON round-trip replays the transition");
Require(restoredReplayed.Value!.Child.Value!.Count.Value == 9, "ChangeSet JSON round-trip replays nested members");

Console.WriteLine("SparseFragments README consumer passed.");

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

// Mirrors the README Quick Start model. Reachable partial nested models
// auto-generate Fragment/Patch, so Child stays undecorated but partial because
// the samples construct Child.Fragment directly.
[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public Child? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}

public partial class Child
{
    public int Count { get; set; }
    public string Host { get; set; } = "localhost";
}
