using System.Text.Json;
using SparseFragments;

// Canonical .NET Framework 4.8 compatibility consumer (#49).
// Consumes the packed SparseFragments release candidate (no ProjectReference
// fallback when SparseFragmentsPackageVersion is set) and executes
// representative generated/runtime behavior inside a real .NET Framework
// process: fragments, nested patches, merge, deep clone, collections, and a
// ChangeSet payload JSON round-trip (so System.Text.Json is loaded and exercised too).
var original = NetFxSettings.Fragment.From(
    new NetFxSettings
    {
        Label = "original",
        Child = new NetFxChild { Count = 7, Host = "keep" },
        Tags = new List<string> { "a", "b" },
        Scores = new Dictionary<string, int> { ["x"] = 1 },
    }
);
Require(original.Label.Value == "original", "fragment from model");

var patch = new NetFxSettings.Patch { Label = (string?)null };
patch.Child.Count = 9;
var result = original.Apply(patch);
Require(result.Label.IsPresent && result.Label.Value is null, "present null");
Require(result.Child.Value!.Count.Value == 9, "typed nested Set");
Require(result.Child.Value.Host.Value == "keep", "unchanged child member");

var clone = original.ToModel().DeepClone();
clone.Child!.Count = 42;
Require(original.ToModel().Child!.Count == 7, "structural deep clone isolation");

var tagsPatch = new NetFxSettings.Patch { Tags = new List<string> { "c" } };
var tagged = original.Apply(tagsPatch);
Require(tagged.Tags.Value!.Count == 1 && tagged.Tags.Value[0] == "c", "collection replace");

var dictPatch = new NetFxSettings.Patch
{
    Scores = new Dictionary<string, int> { ["x"] = 2, ["y"] = 3 },
};
var scored = original.Apply(dictPatch);
Require(scored.Scores.Value!["x"] == 2 && scored.Scores.Value!["y"] == 3, "dictionary replace");

var before = Optional<NetFxSettings.Fragment?>.Present(original);
var edits = new NetFxSettings.Patch { Label = "edited" };
var inverted = edits.Invert(before).Apply(edits.Apply(before));
Require(inverted.Value!.Label.Value == "original", "invert round-trip");

var changes = NetFxSettings.ChangeSet.Between(before, edits.Apply(before));
var changesJson = JsonSerializer.Serialize(changes.ToPayload());
var imported = JsonSerializer.Deserialize<NetFxSettings.ChangePayload>(changesJson)?.ToChangeSet();
if (imported is null)
{
    throw new InvalidOperationException("Failed: ChangeSet payload JSON deserialize");
}
var roundTripped = imported.ToPatch().Apply(before);
Require(
    NetFxSettings.Patch.Between(roundTripped, edits.Apply(before)).IsEmpty,
    "ChangeSet payload JSON semantic round-trip"
);

// Patch algebra: composed patch matches sequential apply (nested + presence transition).
var first = new NetFxSettings.Patch { Label = "composed-first" };
first.Child.Count = 11;
var second = new NetFxSettings.Patch { Label = (string?)null };
second.Child.Host = "composed-next";
var composed = first.Compose(second);
var viaComposed = composed.Apply(before);
var viaSequential = second.Apply(first.Apply(before));
Require(Same(viaComposed, viaSequential), "compose matches sequential apply");
Require(
    viaComposed.Value!.Child.Value!.Count.Value == 11
        && viaComposed.Value.Child.Value.Host.Value == "composed-next",
    "compose nested members"
);
Require(
    viaComposed.Value.Label.IsPresent && viaComposed.Value.Label.Value is null,
    "compose presence transition"
);

// Structured rebase: disjoint local/nested edit merges cleanly onto upstream label edit.
var upstream = original.ToBuilder();
upstream.Label = Optional<string?>.Present("upstream");
var current = Optional<NetFxSettings.Fragment?>.Present(upstream.Build());
var local = new NetFxSettings.Patch();
local.Child.Count = 12;
var rebased = NetFxSettings.Patch.Rebase(before, local, current);
Require(!rebased.HasConflicts, "rebase disjoint merge");
var replayed = rebased.Rebased.Apply(current);
Require(
    replayed.Value!.Label.Value == "upstream"
        && replayed.Value.Child.Value!.Count.Value == 12
        && replayed.Value.Child.Value.Host.Value == "keep",
    "rebase replay on current"
);

// Concurrent scalar edit on the same member reports a structured conflict.
var conflictLocal = new NetFxSettings.Patch { Label = "local" };
var conflictBuilder = original.ToBuilder();
conflictBuilder.Label = Optional<string?>.Present("current");
var conflictCurrent = Optional<NetFxSettings.Fragment?>.Present(conflictBuilder.Build());
var conflicted = NetFxSettings.Patch.Rebase(before, conflictLocal, conflictCurrent);
Require(conflicted.HasConflicts, "rebase conflict detection");
Require(
    conflicted.Conflicts.Count == 1
        && conflicted.Conflicts[0].Kind == SparseConflictKind.Scalar
        && conflicted.Conflicts[0].PathText == "Label",
    "rebase structured conflict"
);

Console.WriteLine("SparseFragments net48 consumer passed.");

static bool Same(Optional<NetFxSettings.Fragment?> left, Optional<NetFxSettings.Fragment?> right) =>
    NetFxSettings.Patch.Between(left, right).IsEmpty;

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

[SparseFragmentModel]
public partial class NetFxSettings
{
    public string? Label { get; set; }

    public NetFxChild? Child { get; set; }

    public List<string> Tags { get; set; } = new();

    public Dictionary<string, int> Scores { get; set; } = new();
}

public partial class NetFxChild
{
    public int Count { get; set; }

    public string Host { get; set; } = "localhost";
}
