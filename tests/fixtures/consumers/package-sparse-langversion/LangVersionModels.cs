using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SparseFragments;

// C# 9.0 minimum-language gate (#116). This source must stay C# 9-compatible:
// block-scoped namespace, explicit usings, no collection expressions, no
// record structs. The models below cover the representative generated
// surface: ordinary and nested partial models, nullable members, an init-only
// member, merge collections, a keyed collection, and ChangeSet JSON payloads.
namespace LangVersionProbe
{
    public static class LangVersionCheck
    {
        public static string Run()
        {
            var original = ProbeSettings.Fragment.From(
                new ProbeSettings
                {
                    Label = "original",
                    Code = "v1",
                    Child = new ProbeChild { Count = 7, Host = "keep" },
                    Plugins = new List<string> { "base-plugin" },
                    Tags = new HashSet<string>(StringComparer.Ordinal) { "base-tag" },
                    Options = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["theme"] = "dark",
                    },
                    Quests = new List<ProbeQuest>
                    {
                        new ProbeQuest { Slug = "a", Title = "A" },
                    },
                }
            );

            // Sparse construction: only set members become present.
            var sparse = new ProbeSettings.Fragment { Label = "override" };
            Require(sparse.Label.IsPresent, "sparse construction marks members present");
            Require(!sparse.Child.IsPresent, "sparse construction leaves members missing");

            // Init-only members round-trip through From/ToModel.
            Require(original.ToModel().Code == "v1", "init-only member round-trip");

            // Layered merge: Append concatenates, SetUnion unions, Deep merges nested.
            var higher = new ProbeSettings.Fragment
            {
                Child = new ProbeChild.Fragment { Count = 9 },
                Plugins = new List<string> { "extra-plugin" },
                Tags = new HashSet<string>(StringComparer.Ordinal) { "extra-tag" },
            };
            var merged = original.Merge(higher).ToModel();
            Require(merged.Child != null && merged.Child.Host == "keep", "merge keeps nested members");
            Require(merged.Child != null && merged.Child.Count == 9, "merge higher priority wins");
            Require(
                merged.Plugins.Count == 2 && merged.Plugins[1] == "extra-plugin",
                "merge Append concatenates");
            Require(
                merged.Tags.SetEquals(
                    new HashSet<string>(StringComparer.Ordinal) { "base-tag", "extra-tag" }),
                "merge SetUnion combines sets");
            Require(merged.Options["theme"] == "dark", "merge keeps Replace dictionary");

            // Typed patch: present null, nested Set/Remove/SetNull.
            var patch = new ProbeSettings.Patch { Label = (string?)null };
            patch.Child.Count = 9;
            var result = original.Apply(patch);
            Require(result.Label.IsPresent && result.Label.Value is null, "typed patch present null");
            Require(result.Child.Value != null && result.Child.Value.Count.Value == 9, "typed nested set");
            Require(result.Child.Value != null && result.Child.Value.Host.Value == "keep", "typed patch keeps members");

            var remove = new ProbeSettings.Patch();
            remove.Child.Remove();
            Require(!original.Apply(remove).Child.IsPresent, "typed nested Remove");
            var toNull = new ProbeSettings.Patch();
            toNull.Child.SetNull();
            var nulled = original.Apply(toNull);
            Require(nulled.Child.IsPresent && nulled.Child.Value is null, "typed nested SetNull");

            // Keyed collection add/remove/edit by stable identity.
            var beforeQuests = new ProbeSettings
            {
                Quests = new List<ProbeQuest>
                {
                    new ProbeQuest { Slug = "a", Title = "A" },
                    new ProbeQuest { Slug = "b", Title = "B" },
                },
            };
            var afterQuests = new ProbeSettings
            {
                Quests = new List<ProbeQuest>
                {
                    new ProbeQuest { Slug = "b", Title = "B2" },
                    new ProbeQuest { Slug = "c", Title = "C" },
                },
            };
            var questChanges = ProbeSettings.ChangeSet.Between(beforeQuests, afterQuests);
            Require(!questChanges.IsEmpty, "keyed Between detects changes");
            Require(
                questChanges.Quests.Added.Count == 1 && questChanges.Quests.Added[0].Slug == "c",
                "keyed Added carries new element");
            Require(
                questChanges.Quests.Removed.Count == 1 && questChanges.Quests.Removed[0].Slug == "a",
                "keyed Removed carries old element");
            Require(
                questChanges.Quests.Edited.ContainsKey("b")
                    && questChanges.Quests.Edited["b"].Title.After.Value == "B2",
                "keyed Edited carries nested transition");

            // Patch algebra: compose, invert, sparse Between, structured rebase.
            var basis = Optional<ProbeSettings.Fragment?>.Present(original);
            var edits = new ProbeSettings.Patch { Label = (string?)null };
            edits.Child.Host = "edited";
            var next = new ProbeSettings.Patch();
            next.Child.Count = 11;
            var combined = edits.Compose(next);
            var applied = combined.Apply(basis);
            var restored = combined.Invert(basis).Apply(applied);
            Require(
                restored.Value != null && restored.Value.Label.Value == "original",
                "exact inversion restores label");
            Require(
                applied.Value != null
                    && applied.Value.Child.Value != null
                    && applied.Value.Child.Value.Count.Value == 11,
                "patch composition applies nested edit");
            var between = ProbeSettings.Patch.Between(basis, applied);
            Require(!between.IsEmpty, "sparse Between detects patch");

            var upstream = original.ToBuilder();
            upstream.Label = Optional<string?>.Present("upstream");
            var local = new ProbeSettings.Patch();
            local.Child.Count = 12;
            var rebased = ProbeSettings.Patch.Rebase(
                basis,
                local,
                Optional<ProbeSettings.Fragment?>.Present(upstream.Build()));
            Require(!rebased.HasConflicts, "structured rebase");

            // Builders and structural DeepClone.
            var builder = original.ToBuilder();
            builder.Label = Optional<string?>.Missing;
            Require(!builder.Build().Label.IsPresent, "builder copy without member");
            var clone = original.ToModel().DeepClone();
            Require(clone.Child != null && clone.Child.Count == 7, "DeepClone copies nested model");
            if (clone.Child != null)
            {
                clone.Child.Count = 42;
            }
            Require(original.ToModel().Child != null && original.ToModel().Child!.Count == 7, "DeepClone model isolation");

            // ChangeSet payload JSON transport with ordinary System.Text.Json.
            var baseline = new ProbeSettings.Fragment { Label = "base" };
            var baselineOpt = Optional<ProbeSettings.Fragment?>.Present(baseline);
            var editedBaseline = new ProbeSettings.Fragment { Label = "patched" };
            var changes = ProbeSettings.ChangeSet.Between(
                baselineOpt,
                Optional<ProbeSettings.Fragment?>.Present(editedBaseline));
            var changesJson = JsonSerializer.Serialize(changes.ToPayload());
            var restoredChanges = JsonSerializer
                .Deserialize<ProbeSettings.ChangePayload>(changesJson)
                ?.ToChangeSet();
            if (restoredChanges is null)
            {
                throw new InvalidOperationException("Failed: ChangeSet payload JSON deserialize");
            }
            Require(
                restoredChanges.ToPatch().Apply(baselineOpt).Value != null
                    && restoredChanges.ToPatch().Apply(baselineOpt).Value!.Label.Value == "patched",
                "ChangeSet payload JSON round-trip");
            Require(changesJson.Contains("patched"), "ChangeSet payload JSON export");

            // Model-baseline ChangeSet surface: FromPatch, ApplyTo, TryApplyTo.
            var modelBefore = new ProbeSettings { Label = "before" };
            var modelAfter = new ProbeSettings { Label = "after" };
            var modelChanges = ProbeSettings.ChangeSet.Between(modelBefore, modelAfter);
            var modelPatch = new ProbeSettings.Patch { Label = "patched-model" };
            Require(
                !ProbeSettings.ChangeSet.FromPatch(modelBefore, modelPatch).IsEmpty,
                "model-baseline FromPatch");
            Require(
                modelPatch.ApplyTo(modelBefore).Label == "patched-model",
                "model Patch.ApplyTo");
            Require(modelBefore.Label == "before", "model Patch.ApplyTo isolation");
            if (!modelChanges.TryApplyTo(modelBefore.DeepClone(), out var updatedModel))
            {
                throw new InvalidOperationException("Failed: conflict-free model application");
            }
            Require(updatedModel.Label == "after", "ChangeSet model application");

            return "SparseFragments C# 9.0 consumer passed.";
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
    public partial class ProbeSettings
    {
        public string? Label { get; set; }

        public string? Code { get; init; }

        public ProbeChild? Child { get; set; }

        [SparseMerge(MergeMode.Append)]
        public List<string> Plugins { get; set; } = new List<string>();

        [SparseMerge(MergeMode.SetUnion)]
        public HashSet<string> Tags { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        public Dictionary<string, string> Options { get; set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public List<ProbeQuest> Quests { get; set; } = new List<ProbeQuest>();
    }

    public partial class ProbeChild
    {
        public int Count { get; set; }

        public string Host { get; set; } = "localhost";
    }

    public partial class ProbeQuest
    {
        [SparseKey]
        public string Slug { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
    }
}
