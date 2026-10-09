using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Tests.Shared.RelocationWorld;

namespace SparseFragments.Tests.Shared
{
    // Relocation contract for issue #194 (stage 4 of #176): model-specific
    // Patch (apply, compose, invert, rebase and nested/keyed/dictionary
    // operations) and ChangeSet (Between, Compose, Invert, Rebase, Apply,
    // EnumerateChanges, projection and restoration) algorithms live in the
    // external per-model operation container, while the model keeps ergonomic
    // typed facades. Behavior tests below run against compile-time generated
    // models in this assembly, so every law is exercised through the new
    // facade/operations boundary; shape tests pin the split itself.
    public sealed class PatchChangeSetRelocationTests
    {
        private static Dictionary<string, string> RunProbe()
        {
            var tree = CSharpSyntaxTree.ParseText(
                """
                using SparseFragments;
                using System.Collections.Generic;
                namespace RelocationProbe
                {
                    [SparseFragmentModel]
                    public partial class ProbeChild
                    {
                        public string Name { get; set; } = "";
                    }
                    [SparseFragmentModel]
                    public partial class ProbeRoot
                    {
                        public string Label { get; set; } = "";
                        public ProbeChild? Child { get; set; }
                        public List<ProbeItem> Items { get; set; } = new();
                        public Dictionary<string, string> Scores { get; set; } = new();
                    }
                    [SparseFragmentModel]
                    public partial class ProbeItem
                    {
                        [SparseKey]
                        public string Id { get; set; } = "";
                        public string Name { get; set; } = "";
                    }
                }
                """,
                path: "Models.cs"
            );
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
                Path.PathSeparator
            );
            var references = trusted
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToList();
            references.Add(
                MetadataReference.CreateFromFile(
                    typeof(SparseFragmentModelAttribute).Assembly.Location
                )
            );
            var compilation = CSharpCompilation.Create(
                "RelocationProbe",
                [tree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            updated
                .GetDiagnostics()
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty();
            return driver
                .GetRunResult()
                .Results.SelectMany(static result => result.GeneratedSources)
                .ToDictionary(
                    static source => source.HintName,
                    static source => source.SourceText.ToString(),
                    StringComparer.Ordinal
                );
        }

        [Test]
        public void OperationsLiveOutsideTheModel()
        {
            var sources = RunProbe();
            var surface = sources.Values.Single(text =>
                text.Contains("sealed class Patch", StringComparison.Ordinal)
                && text.Contains("namespace RelocationProbe", StringComparison.Ordinal)
                && text.Contains("ProbeRoot", StringComparison.Ordinal)
            );
            var implementation = sources.Values.Single(text =>
                text.Contains(
                    "Per-model generated operations for 'global::RelocationProbe.ProbeRoot'"
                )
            );

            // The implementation file carries both operation containers.
            implementation.ShouldContain("class PatchOperations");
            implementation.ShouldContain("class ChangeSetOperations");

            // Surface and implementation hints stay stable and distinct.
            var hints = sources
                .Keys.Where(static key => key.Contains("ProbeRoot", StringComparison.Ordinal))
                .ToArray();
            hints.Length.ShouldBe(2);
            hints
                .Count(static key => key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal))
                .ShouldBe(1);
            surface.ShouldContain("sealed class Patch");
        }

        [Test]
        public void FacadesKeepErgonomicSignaturesAndDelegate()
        {
            var sources = RunProbe();
            var surface = sources.Values.Single(text =>
                text.Contains("sealed class Patch", StringComparison.Ordinal)
                && text.Contains("namespace RelocationProbe", StringComparison.Ordinal)
                && text.Contains("ProbeRoot", StringComparison.Ordinal)
            );
            var implementation = sources.Values.Single(text =>
                text.Contains(
                    "Per-model generated operations for 'global::RelocationProbe.ProbeRoot'"
                )
            );

            // Typed facade surface is preserved: ref member access, lazy nested
            // identity, whole operations and the public algebra entry points.
            surface.ShouldContain("public ref ");
            surface.ShouldContain("public static Patch ");
            surface.ShouldContain("public static ChangeSet Between(");
            surface.ShouldContain("public ChangeSet Compose(ChangeSet next)");
            surface.ShouldContain("EnumerateChanges()");

            // Facade methods delegate into the operation container.
            surface.ShouldContain("PatchOperations.");
            surface.ShouldContain("ChangeSetOperations.");

            // Heavyweight bodies no longer live under the model.
            surface.ShouldNotContain("ApplyMembers(Patch self");
            surface.ShouldNotContain("ToPatch(ChangeSet self");
            surface.ShouldNotContain("EnumerateChanges(ChangeSet self");

            // The algorithms live in the implementation source instead.
            implementation.ShouldContain("ApplyMembers(Patch self");
            implementation.ShouldContain("ToPatch(ChangeSet self");
            implementation.ShouldContain("EnumerateChanges(ChangeSet self");
            implementation.ShouldContain("ToPayload(ChangeSet self");
            implementation.ShouldContain("Compose(ChangeSet self, ChangeSet next)");
            implementation.ShouldContain("Compose(Patch self, Patch next)");
        }

        [Test]
        public void PatchRefMemberPreservesMutationIdentity()
        {
            var patch = new Parent.Patch();

            // Typed ref access reaches the canonical facade field: mutation
            // through the reference is visible through the patch itself.
            ref var slot = ref patch.Title;
            slot = FragmentOperation<string?>.Set("hello");
            patch.Title.Kind.ShouldBe(FragmentOperationKind.Set);
            patch.Title.Value.ShouldBe("hello");

            slot = FragmentOperation<string?>.Remove;
            patch.Title.Kind.ShouldBe(FragmentOperationKind.Remove);

            // Lazy nested identity is preserved: repeated access aliases one patch.
            ReferenceEquals(patch.Child, patch.Child).ShouldBeTrue();
            patch.Child.Name = "nested";
            patch.Child.Name.Kind.ShouldBe(FragmentOperationKind.Set);

            var applied = Parent
                .Fragment.From(
                    new Parent
                    {
                        Title = "base",
                        Child = new Child { Name = "base" },
                    }
                )
                .Apply(patch)
                .ToModel();
            applied.Title.ShouldBeNull();
            applied.Child!.Name.ShouldBe("nested");
        }

        [Test]
        public void PatchAlgebraLawsHoldAcrossTheBoundary()
        {
            var before = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment
                {
                    Title = Optional<string?>.Present("a"),
                    Count = Optional<int>.Present(1),
                }
            );
            var after = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment
                {
                    Title = Optional<string?>.Present("b"),
                    Count = Optional<int>.Present(2),
                }
            );

            // Between/Apply round trip.
            var patch = Parent.Patch.Between(before, after);
            Parent.Patch.Between(before, before).IsEmpty.ShouldBeTrue();
            var roundTripped = patch.Apply(before).Value!.ToModel();
            roundTripped.Title.ShouldBe("b");
            roundTripped.Count.ShouldBe(2);

            // Compose equivalence: sequential application equals composed application.
            var middle = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment
                {
                    Title = Optional<string?>.Present("a"),
                    Count = Optional<int>.Present(9),
                }
            );
            var first = Parent.Patch.Between(before, middle);
            var second = Parent.Patch.Between(middle, after);
            var composed = first.Compose(second);
            var sequential = second.Apply(first.Apply(before)).Value!.ToModel();
            var combined = composed.Apply(before).Value!.ToModel();
            combined.Title.ShouldBe(sequential.Title);
            combined.Count.ShouldBe(sequential.Count);

            // Invert restores the baseline.
            var inverse = patch.Invert(before);
            var restored = inverse.Apply(patch.Apply(before)).Value!.ToModel();
            restored.Title.ShouldBe("a");
            restored.Count.ShouldBe(1);

            // Null/missing whole semantics are preserved.
            var missing = Optional<Parent.Fragment?>.Missing;
            var toMissing = Parent.Patch.Between(before, missing);
            toMissing.Apply(before).IsPresent.ShouldBeFalse();
            var nullPatch = new Parent.Patch();
            nullPatch.SetNull();
            nullPatch.Apply(before).Value.ShouldBeNull();
        }

        [Test]
        public void PatchRebaseLawsHoldAcrossTheBoundary()
        {
            var basis = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment { Title = Optional<string?>.Present("base") }
            );
            var desired = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment { Title = Optional<string?>.Present("local") }
            );
            var local = Parent.Patch.Between(basis, desired);

            // Clean rebase onto an untouched concurrent state succeeds empty of conflicts.
            var clean = Parent.Patch.Rebase(basis, local, basis);
            clean.Conflicts.ShouldBeEmpty();

            // A concurrent conflicting change reports a structured conflict.
            var concurrent = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment { Title = Optional<string?>.Present("remote") }
            );
            var conflicted = Parent.Patch.Rebase(basis, local, concurrent);
            conflicted.Conflicts.ShouldNotBeEmpty();
            conflicted.Conflicts[0].Path.ShouldBe(["Title"]);
        }

        [Test]
        public void ChangeSetLawsHoldAcrossTheBoundary()
        {
            var first = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment { Title = Optional<string?>.Present("a") }
            );
            var second = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment { Title = Optional<string?>.Present("b") }
            );
            var third = Optional<Parent.Fragment?>.Present(
                new Parent.Fragment { Title = Optional<string?>.Present("c") }
            );

            var forward = Parent.ChangeSet.Between(first, second);
            forward.IsEmpty.ShouldBeFalse();
            Parent.ChangeSet.Between(first, first).IsEmpty.ShouldBeTrue();

            // Typed transitions project the canonical endpoints.
            forward.Title.Before.Value.ShouldBe("a");
            forward.Title.After.Value.ShouldBe("b");
            forward.Title.IsChanged.ShouldBeTrue();

            // Compose chains contiguous transitions; invert swaps direction.
            var next = Parent.ChangeSet.Between(second, third);
            var composed = forward.Compose(next);
            composed.Title.After.Value.ShouldBe("c");
            var inverted = forward.Invert();
            inverted.Title.Before.Value.ShouldBe("b");
            inverted.Title.After.Value.ShouldBe("a");

            // Patch projection round-trips through the facade.
            var patch = composed.ToPatch();
            var restored = Parent.ChangeSet.FromPatch(first, patch);
            restored.Title.After.Value.ShouldBe("c");

            // Baseline advancement validates and applies.
            var advanced = composed.ApplyToBaseline(first);
            advanced.Value!.ToModel().Title.ShouldBe("c");

            // Enumeration and paths observe the same canonical state.
            var changes = composed.EnumerateChanges().ToArray();
            changes.Length.ShouldBe(1);
            changes[0].Path.ShouldBe("Title");
            composed.EnumerateChangedPaths().ShouldBe(["Title"]);

            // Rebase onto a matching state succeeds.
            var rebased = composed.RebaseOnto(first);
            rebased.HasConflicts.ShouldBeFalse();
        }

        [Test]
        public void KeyedAndDictionaryBehaviorIsPreserved()
        {
            var before = new Roster
            {
                Label = "v1",
                Items = [new RosterItem { Id = "a", Name = "ann" }],
                Scores = new Dictionary<string, string> { ["k"] = "1" },
            };
            var after = new Roster
            {
                Label = "v2",
                Items =
                [
                    new RosterItem { Id = "b", Name = "bob" },
                    new RosterItem { Id = "a", Name = "ann2" },
                ],
                Scores = new Dictionary<string, string> { ["k"] = "2" },
            };

            var changes = Roster.ChangeSet.Between(before, after);
            changes.IsEmpty.ShouldBeFalse();

            // Canonical sparse keyed/dictionary transitions keep items and
            // orders without whole-value snapshots.
            var items = changes.Items;
            items.IsChanged.ShouldBeTrue();
            items.Added.Count.ShouldBe(1);
            items.Removed.Count.ShouldBe(0);
            items.OrderChanged.ShouldBeTrue();

            // Order-only changes compose and project without value edits.
            var reorderPatch = new Roster.Patch();
            reorderPatch.Items.SetOrder(["b", "a"]);
            var reordered = Roster.Fragment.From(after).Apply(reorderPatch).ToModel();
            reordered.Items.Select(static item => item.Id).ShouldBe(["b", "a"]);

            // Removal and dictionary edits apply through the boundary.
            var edit = new Roster.Patch();
            edit.Items.Remove("b");
            edit.Scores.SetEntry("k", "3");
            var edited = Roster.Fragment.From(after).Apply(edit).ToModel();
            edited.Items.Count.ShouldBe(1);
            edited.Items[0].Name.ShouldBe("ann2");
            edited.Scores["k"].ShouldBe("3");

            // Keyed rebase reports structured conflicts on concurrent edits.
            var basis = Roster.Fragment.From(before);
            var local = new Roster.Patch();
            local.Items.Edit("a").Name = "local";
            var concurrent = Optional<Roster.Fragment?>.Present(
                new Roster.Fragment
                {
                    Items = Optional<List<RosterItem>>.Present([
                        new RosterItem { Id = "a", Name = "remote" },
                    ]),
                }
            );
            var rebased = Roster.Patch.Rebase(
                Optional<Roster.Fragment?>.Present(basis),
                local,
                concurrent
            );
            rebased.Conflicts.ShouldNotBeEmpty();
        }

        [Test]
        public void ChangePayloadJsonRoundTripSurvivesRelocation()
        {
            var before = new Parent { Title = "a", Count = 1 };
            var after = new Parent { Title = "b", Count = 2 };
            var payload = before.CreateChangeSet(after).ToPayload();
            var json = JsonSerializer.Serialize(payload);
            var restored = JsonSerializer.Deserialize<Parent.ChangePayload>(json);
            restored.ShouldNotBeNull();
            var roundTripped = Parent.ChangeSet.FromPayload(restored!);
            roundTripped.Title.After.Value.ShouldBe("b");

            // The baseline-free projection still reaches the same after-state.
            var patch = payload.ToPatch();
            var applied = Parent.Fragment.From(before).Apply(patch).ToModel();
            applied.Title.ShouldBe("b");
            applied.Count.ShouldBe(2);
        }
    }
}

namespace SparseFragments.Tests.Shared.RelocationWorld
{
    [SparseFragmentModel]
    public partial class Child
    {
        public string Name { get; set; } = string.Empty;
    }

    [SparseFragmentModel]
    public partial class Parent
    {
        public string? Title { get; set; }

        public int Count { get; set; }

        public Child? Child { get; set; }
    }

    [SparseFragmentModel]
    public partial class RosterItem
    {
        [SparseKey]
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    [SparseFragmentModel]
    public partial class Roster
    {
        public string Label { get; set; } = string.Empty;

        public List<RosterItem> Items { get; set; } = [];

        public Dictionary<string, string> Scores { get; set; } = new();
    }
}
