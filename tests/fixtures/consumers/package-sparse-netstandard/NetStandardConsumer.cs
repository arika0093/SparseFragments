using System;
using System.Collections.Generic;
using System.Text.Json;
using SparseFragments;

// Lowest-shipped-TFM consumer (#27): a netstandard2.0 class library that
// references the packed SparseFragments package and compiles generated
// Fragment/Patch code. Build-only (netstandard2.0 has no runnable host), so
// compilation success is the gate: it fails if generated code references APIs
// unavailable to netstandard2.0.
//
// The models below are representative set/dictionary/clone/patch shapes that
// exercise the target-dependent generator branches (notably whether the
// capacity+comparer HashSet<T> constructor exists in the consumer
// compilation): an Append list, a SetUnion hash set, a Replace dictionary,
// a Deep nested model, plus typed patch algebra, builders, DeepClone, and
// ChangeSet JSON transport.
public static class NetStandardConsumerCheck
{
    public static string Run()
    {
        var original = NetStandardSettings.Fragment.From(
            new NetStandardSettings
            {
                Label = "original",
                Child = new NetStandardChild { Count = 7, Host = "keep" },
                Plugins = new List<string> { "base-plugin" },
                Tags = new HashSet<string>(StringComparer.Ordinal) { "base-tag" },
                Options = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["theme"] = "dark",
                },
            }
        );

        // Sparse construction: only set members become present.
        var sparse = new NetStandardSettings.Fragment { Label = "override" };
        Require(sparse.Label.IsPresent, "sparse construction marks members present");
        Require(!sparse.Child.IsPresent, "sparse construction leaves members missing");

        // Layered merge: Append concatenates, SetUnion unions, Deep merges nested.
        var higher = new NetStandardSettings.Fragment
        {
            Child = new NetStandardChild.Fragment { Count = 9 },
            Plugins = new List<string> { "extra-plugin" },
            Tags = new HashSet<string>(StringComparer.Ordinal) { "extra-tag" },
        };
        var merged = original.Merge(higher).ToModel();
        Require(merged.Label == "original", "merge keeps lower value for missing members");
        Require(merged.Child!.Host == "keep", "merge falls through nested missing members");
        Require(merged.Child.Count == 9, "merge higher priority wins");
        Require(
            merged.Plugins.Count == 2
                && merged.Plugins[0] == "base-plugin"
                && merged.Plugins[1] == "extra-plugin",
            "merge Append concatenates"
        );
        Require(
            merged.Tags.SetEquals(new HashSet<string>(StringComparer.Ordinal) { "base-tag", "extra-tag" }),
            "merge SetUnion combines sets"
        );
        Require(merged.Options["theme"] == "dark", "merge keeps Replace dictionary");

        // Typed patch: present null, nested Set/Unset/SetNull.
        var patch = new NetStandardSettings.Patch { Label = (string?)null };
        patch.Child.Count = 9;
        var result = original.Apply(patch);
        Require(result.Label.IsPresent && result.Label.Value is null, "typed patch present null");
        Require(result.Child.Value!.Count.Value == 9, "typed nested set");
        Require(result.Child.Value.Host.Value == "keep", "typed patch keeps unspecified members");

        var remove = new NetStandardSettings.Patch();
        remove.Child.Unset();
        Require(!original.Apply(remove).Child.IsPresent, "typed nested Unset");
        var toNull = new NetStandardSettings.Patch();
        toNull.Child.SetNull();
        var nulled = original.Apply(toNull);
        Require(nulled.Child.IsPresent && nulled.Child.Value is null, "typed nested SetNull");

        // Diff and patch algebra.
        var before = new NetStandardSettings { Label = "before" };
        var after = new NetStandardSettings { Label = "after" };
        var diff = NetStandardSettings.Fragment.Diff(before, after);
        Require(
            NetStandardSettings.Fragment.From(before).ApplyChanges(diff).Label.Value == "after",
            "Diff/ApplyChanges"
        );
        var basis = Optional<NetStandardSettings.Fragment?>.Present(original);
        var edits = new NetStandardSettings.Patch { Label = (string?)null };
        var next = new NetStandardSettings.Patch();
        next.Child.Host = "next";
        var combined = edits.Compose(next);
        var applied = combined.Apply(basis);
        var restored = combined.Invert(basis).Apply(applied);
        Require(
            restored.Value!.Label.Value == "original"
                && restored.Value.Child.Value!.Count.Value == 7,
            "exact inversion"
        );
        var between = NetStandardSettings.Patch.Between(basis, applied);
        Require(
            between.Apply(basis).Value!.Child.Value!.Host.Value == "next",
            "sparse Between"
        );

        // Structured rebase.
        var upstream = original.ToBuilder();
        upstream.Label = Optional<string?>.Present("upstream");
        var local = new NetStandardSettings.Patch();
        local.Child.Count = 12;
        var rebased = NetStandardSettings.Patch.Rebase(
            basis,
            local,
            Optional<NetStandardSettings.Fragment?>.Present(upstream.Build())
        );
        Require(!rebased.HasConflicts, "structured rebase");

        // Builders and structural DeepClone (exercises set/dictionary clone helpers).
        var builder = original.ToBuilder();
        builder.Label = Optional<string?>.Missing;
        Require(!builder.Build().Label.IsPresent, "builder copy without member");
        var clone = original.ToModel().DeepClone();
        clone.Child!.Count = 42;
        clone.Tags.Add("mutated");
        clone.Options["theme"] = "light";
        Require(original.ToModel().Child!.Count == 7, "DeepClone model isolation");
        Require(!original.ToModel().Tags.Contains("mutated"), "DeepClone set isolation");
        Require(original.ToModel().Options["theme"] == "dark", "DeepClone dictionary isolation");
        var fragmentClone = original.DeepClone();
        Require(fragmentClone.Label.Value == "original", "fragment DeepClone");

        // ChangeSet JSON transport (standard System.Text.Json over the generated converters).
        var baseline = new NetStandardSettings.Fragment { Label = "base" };
        var baselineOpt = Optional<NetStandardSettings.Fragment?>.Present(baseline);
        var editedBaseline = new NetStandardSettings.Fragment { Label = "patched" };
        var changes = NetStandardSettings.ChangeSet.Between(
            baselineOpt,
            Optional<NetStandardSettings.Fragment?>.Present(editedBaseline));
        var changesJson = JsonSerializer.Serialize(changes);
        var restoredChanges = JsonSerializer.Deserialize<NetStandardSettings.ChangeSet>(changesJson);
        if (restoredChanges is null)
        {
            throw new InvalidOperationException("Failed: ChangeSet JSON deserialize");
        }
        Require(
            restoredChanges.ToPatch().Apply(baselineOpt).Value!.Label.Value == "patched",
            "ChangeSet JSON round-trip"
        );
        Require(changesJson.Contains("patched"), "ChangeSet JSON export");

        var modelBefore = new NetStandardSettings
        {
            Label = "before",
            Child = new NetStandardChild { Count = 7, Host = "model-host" },
        };
        var modelAfter = new NetStandardSettings
        {
            Label = "after",
            Child = new NetStandardChild { Count = 9, Host = "model-host" },
        };
        var modelChanges = NetStandardSettings.ChangeSet.Between(modelBefore, modelAfter);
        var modelPatch = new NetStandardSettings.Patch { Label = "patched-model" };
        var fromModelPatch = NetStandardSettings.ChangeSet.FromPatch(modelBefore, modelPatch);
        Require(!fromModelPatch.IsEmpty, "model-baseline FromPatch");
        var appliedModel = modelPatch.ApplyTo(modelBefore);
        Require(appliedModel.Label == "patched-model", "model Patch.ApplyTo");
        Require(modelBefore.Label == "before", "model Patch.ApplyTo isolation");

        var currentModel = modelBefore.DeepClone();
        currentModel.Child!.Host = "server-host";
        if (modelChanges.TryApplyTo(currentModel, out var updatedModel))
        {
            Require(updatedModel.Child!.Count == 9, "ChangeSet model application");
            Require(updatedModel.Child.Host == "server-host", "ChangeSet disjoint rebase");
        }
        else
        {
            throw new InvalidOperationException("Failed: conflict-free model application");
        }

        var conflictingModel = modelBefore.DeepClone();
        conflictingModel.Label = "server-label";
        if (
            modelChanges.TryApplyTo(
                conflictingModel,
                out var conflictedModel,
                out var modelConflicts
            )
        )
        {
            throw new InvalidOperationException("Failed: model conflict should be reported");
        }
        Require(conflictedModel is null, "conflicted application has no model");
        Require(modelConflicts.Count > 0, "model application returns conflict details");

        return "SparseFragments netstandard2.0 consumer passed.";
    }

    private static void Require(bool condition, string capability)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Failed: " + capability);
        }
    }
}

[SparseFragmentModel]
public partial class NetStandardSettings
{
    public string? Label { get; set; }

    public NetStandardChild? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public List<string> Plugins { get; set; } = new List<string>();

    [SparseMerge(MergeMode.SetUnion)]
    public HashSet<string> Tags { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public Dictionary<string, string> Options { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public partial class NetStandardChild
{
    public int Count { get; set; }

    public string Host { get; set; } = "localhost";
}
