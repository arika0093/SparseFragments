using System.Text;
using SparseFragments;

// Canonical .NET Framework 4.8 compatibility consumer (#49).
// Consumes the packed SparseFragments release candidate (no ProjectReference
// fallback when SparseFragmentsPackageVersion is set) and executes
// representative generated/runtime behavior inside a real .NET Framework
// process: fragments, nested patches, merge, deep clone, collections, and a
// JSON Patch round-trip (so System.Text.Json is loaded and exercised too).
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

var jsonBytes = edits.ToJsonPatch(before);
var jsonText = Encoding.UTF8.GetString(jsonBytes.ToArray());
Require(jsonText.Contains("edited"), "json patch export");
var imported = NetFxSettings.Patch.FromJsonPatch(before, jsonBytes);
var roundTripped = imported.Apply(before);
Require(
    NetFxSettings.Patch.Between(roundTripped, edits.Apply(before)).IsEmpty,
    "json patch semantic round-trip"
);

Console.WriteLine("SparseFragments net48 consumer passed.");

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
