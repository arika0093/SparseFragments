using System.Text.Json;
using SparseFragments;

// Compile-checked mirrors of annotated Markdown samples (#68).
// Each `// sample: <id>` region matches the same-id fenced block in docs/*.md
// exactly after normalization (using-lines, blank lines, and indentation are
// ignored); verify-docs-samples.sh fails on drift. Regions execute as part of
// Run() so documented results are verified, not just compiled.
public static class VerifiedSamples
{
    public static void Run()
    {
        CoreCreate();
        CoreLayering();
        CoreDiff();
        CorePatch();
        CoreBetween();
        CoreChangeSet();
        CoreAlgebra();
        CoreSerialization();
        KeyedFirst();
        RebaseFirst();
        RebaseApplied();
        RebaseConflict();
        RebaseEndToEnd();
    }

    private static void CoreCreate()
    {
        // sample: core-create
        var current = CounterSettings.Fragment.From(
            new CounterSettings { Label = "a", RetryCount = 1 });

        var sparse = new CounterSettings.Fragment
        {
            RetryCount = 3, // present; Label stays missing
        };

        // sparse.Label.IsPresent == false
        // sparse.RetryCount.Value == 3
        DocsCheck.Require(!sparse.Label.IsPresent, "Label stays missing");
        DocsCheck.Require(sparse.RetryCount.Value == 3, "RetryCount is present");
        // /sample
    }

    private static void CoreLayering()
    {
        // sample: core-layering
        var defaults = new CounterSettings.Fragment { RetryCount = 3 };
        var environment = new CounterSettings.Fragment { RetryCount = 5 };
        var user = new CounterSettings.Fragment { Label = "dark" };

        var effective = defaults.Merge(environment).Merge(user);
        // effective.Label == "dark"
        // effective.RetryCount == 5
        DocsCheck.Require(effective.Label.Value == "dark", "user Label wins");
        DocsCheck.Require(effective.RetryCount.Value == 5, "environment RetryCount wins");
        // /sample
    }

    private static void CoreDiff()
    {
        // sample: core-diff
        var beforeModel = new CounterSettings { Label = "a", RetryCount = 1 };
        var afterModel = new CounterSettings { Label = "a", RetryCount = 2 };

        var diff = CounterSettings.Fragment.Diff(beforeModel, afterModel);
        // diff.Label.IsPresent == false
        // diff.RetryCount.Value == 2
        DocsCheck.Require(!diff.Label.IsPresent, "unchanged Label is missing");
        DocsCheck.Require(diff.RetryCount.Value == 2, "changed RetryCount is present");

        var restored = CounterSettings.Fragment.From(beforeModel).ApplyChanges(diff);
        // restored.RetryCount.Value == 2
        DocsCheck.Require(restored.RetryCount.Value == 2, "ApplyChanges replays the diff");
        // /sample
    }

    private static void CorePatch()
    {
        // sample: core-patch
        var basis = CounterSettings.Fragment.From(
            new CounterSettings { Label = "a", RetryCount = 1 });

        var update = new CounterSettings.Patch { Label = (string?)null };
        var updated = basis.Apply(update);
        // updated.Label.IsPresent == true
        // updated.Label.Value is null
        // updated.RetryCount.Value == 1
        DocsCheck.Require(updated.Label.IsPresent, "explicit null stays present");
        DocsCheck.Require(updated.Label.Value is null, "value is null");
        DocsCheck.Require(updated.RetryCount.Value == 1, "untouched member kept");

        var remove = new CounterSettings.Patch();
        remove.RetryCount.Unset();
        // !remove.Apply(basis).Value!.RetryCount.IsPresent
        DocsCheck.Require(
            !remove.Apply(basis).Value!.RetryCount.IsPresent, "Unset drops the contribution");
        // /sample
    }

    private static void CoreBetween()
    {
        // sample: core-between
        var a = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a" }));
        var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

        var removal = CounterSettings.ChangeSet.Between(a, b); // Label: present → missing
        // !removal.ToPatch().Apply(a).Value!.Label.IsPresent
        DocsCheck.Require(
            !removal.ToPatch().Apply(a).Value!.Label.IsPresent, "Between preserves the removal");
        // /sample
    }

    private static void CoreChangeSet()
    {
        // sample: core-changeset
        var start = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 }));
        var finish = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 }));

        // Patch says "set these values": mutable and baseline-free.
        var desired = new CounterSettings.Patch { Label = "b" };

        // ChangeSet says "these values changed from X to Y": immutable and baseline-aware.
        var transition = CounterSettings.ChangeSet.Between(start, finish);
        // transition.IsEmpty == false
        // transition.ToPatch().Apply(start) replays finish
        DocsCheck.Require(!transition.IsEmpty, "ChangeSet carries the Label change");
        DocsCheck.Require(
            CounterSettings.Patch.Between(transition.ToPatch().Apply(start), finish).IsEmpty,
            "ToPatch replays the transition");

        // ChangeSet.FromPatch attaches a known baseline to an existing patch.
        var fromPatch = CounterSettings.ChangeSet.FromPatch(start, desired);
        // fromPatch.ToPatch().Apply(start) replays finish
        DocsCheck.Require(
            CounterSettings.Patch.Between(fromPatch.ToPatch().Apply(start), finish).IsEmpty,
            "FromPatch derives the same transition");
        // /sample
    }

    private static void CoreAlgebra()
    {
        // sample: core-algebra
        var s0 = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 }));
        var s1 = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 }));
        var s2 = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 }));

        // ChangeSet + ChangeSet -> ChangeSet for contiguous transitions.
        var first = CounterSettings.ChangeSet.Between(s0, s1);
        var second = CounterSettings.ChangeSet.Between(s1, s2);
        var combined = first.Compose(second);
        // combined.ToPatch().Apply(s0) reaches s2
        DocsCheck.Require(
            CounterSettings.Patch.Between(combined.ToPatch().Apply(s0), s2).IsEmpty,
            "composed ChangeSet spans both transitions");

        // ChangeSet inverts without an external baseline.
        var undone = combined.Invert();
        // undone.ToPatch().Apply(s2) walks back to s0
        DocsCheck.Require(
            CounterSettings.Patch.Between(undone.ToPatch().Apply(s2), s0).IsEmpty,
            "inverted ChangeSet walks back");

        // Patch + Patch -> Patch stays baseline-free.
        var local = new CounterSettings.Patch { Label = "x" };
        var more = new CounterSettings.Patch { RetryCount = 5 };
        var both = local.Compose(more);
        // both.Label == "x", both.RetryCount == 5
        DocsCheck.Require(
            both.Label.Value == "x" && both.RetryCount.Value == 5,
            "composed Patch carries both operations");
        // /sample
    }

    private static void CoreSerialization()
    {
        // sample: core-serialization
        var start = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 }));
        var finish = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 }));

        var changes = CounterSettings.ChangeSet.Between(start, finish);

        // SparseFragments defines no transport protocol: use ordinary System.Text.Json.
        var json = JsonSerializer.Serialize(changes);
        var restored = JsonSerializer.Deserialize<CounterSettings.ChangeSet>(json)!;
        // restored.ToPatch().Apply(start) replays finish
        DocsCheck.Require(
            CounterSettings.Patch.Between(restored.ToPatch().Apply(start), finish).IsEmpty,
            "deserialized ChangeSet replays the transition");

        var patchJson = JsonSerializer.Serialize(new CounterSettings.Patch { Label = "b" });
        var patchBack = JsonSerializer.Deserialize<CounterSettings.Patch>(patchJson)!;
        // patchBack.Label == "b"
        DocsCheck.Require(patchBack.Label.Value == "b", "deserialized Patch keeps the operation");
        // /sample
    }

    private static void KeyedFirst()
    {
        // sample: keyed-first
        var before = Fleet.Fragment.From(new Fleet
        {
            Servers = new() { new Server { Id = "a", Host = "old" } },
        });
        var after = Fleet.Fragment.From(new Fleet
        {
            Servers = new() { new Server { Id = "a", Host = "new" }, new Server { Id = "b" } },
        });

        var changes = Fleet.ChangeSet.Between(before, after); // add/remove/edit by key
        var applied = changes.ToPatch().Apply(before);        // original untouched

        // applied.Value!.Servers.Value!.Count == 2
        // applied.Value!.Servers.Value!.Single(s => s.Id == "a").Host == "new"
        DocsCheck.Require(applied.Value!.Servers.Value!.Count == 2, "added element present");
        DocsCheck.Require(
            applied.Value!.Servers.Value!.Single(s => s.Id == "a").Host == "new", "edit by key");
        // /sample
    }

    private static void RebaseFirst()
    {
        // sample: rebase-first
        var baseState = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1, Label = "a" }));
        var editedState = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2, Label = "a" }));
        var currentState = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1, Label = "b" }));

        // The ChangeSet carries its own before-state: only the current state is needed.
        var changes = RebaseSettings.ChangeSet.Between(baseState, editedState);
        var rebased = changes.RebaseOnto(currentState);

        var reconciled = rebased.Patch.ToPatch().Apply(currentState);
        // !rebased.HasConflicts
        // reconciled.Value!.RetryCount.Value == 2
        // reconciled.Value!.Label.Value == "b"
        DocsCheck.Require(!rebased.HasConflicts, "disjoint edits replay cleanly");
        DocsCheck.Require(reconciled.Value!.RetryCount.Value == 2, "local edit kept");
        DocsCheck.Require(reconciled.Value!.Label.Value == "b", "concurrent edit kept");
        // /sample
    }

    private static void RebaseApplied()
    {
        // sample: rebase-applied
        var appliedBase = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1 }));
        var appliedEdited = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2 }));
        var alreadyThere = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2 }));

        // Current == After: the change is already present, so rebase is a no-op.
        var noOp = RebaseSettings.ChangeSet.Between(appliedBase, appliedEdited)
            .RebaseOnto(alreadyThere);
        // !noOp.HasConflicts
        // noOp.Patch.IsEmpty == true
        DocsCheck.Require(!noOp.HasConflicts, "already-applied rebase has no conflicts");
        DocsCheck.Require(noOp.Patch.IsEmpty, "already-applied rebase is a semantic no-op");
        // /sample
    }

    private static void RebaseConflict()
    {
        // sample: rebase-conflict
        var conflictBase = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1 }));
        var conflictEdited = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2 }));
        var conflictCurrent = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 3 }));

        var conflicted = RebaseSettings.ChangeSet.Between(conflictBase, conflictEdited)
            .RebaseOnto(conflictCurrent);

        var conflict = conflicted.Conflicts.Single();
        // conflicted.HasConflicts == true
        // conflict.Kind == SparsePatchConflictKind.Scalar
        // conflict.Path == ["RetryCount"]
        DocsCheck.Require(conflicted.HasConflicts, "divergent edits conflict");
        DocsCheck.Require(conflict.Kind == SparsePatchConflictKind.Scalar, "conflict kind is Scalar");
        DocsCheck.Require(
            conflict.Path.SequenceEqual(new[] { "RetryCount" }), "conflict path names the member");
        DocsCheck.Require(
            Equals(conflict.BaseValue.Value, 1)
                && Equals(conflict.LocalValue.Value, 2)
                && Equals(conflict.CurrentValue.Value, 3),
            "conflict carries base/local/current values");
        // /sample
    }

    private static void RebaseEndToEnd()
    {
        // sample: rebase-e2e
        // Server sends DTO (state A); the client edits A -> B and creates a ChangeSet.
        var stateA = new RebaseSettings { RetryCount = 1, Label = "a" };
        var stateB = new RebaseSettings { RetryCount = 2, Label = "a" };
        var outgoing = RebaseSettings.ChangeSet.Between(
            Optional<RebaseSettings.Fragment?>.Present(RebaseSettings.Fragment.From(stateA)),
            Optional<RebaseSettings.Fragment?>.Present(RebaseSettings.Fragment.From(stateB)));

        // The ChangeSet travels as JSON through the application's own transport.
        var json = JsonSerializer.Serialize(outgoing);
        var incoming = JsonSerializer.Deserialize<RebaseSettings.ChangeSet>(json)!;

        // Meanwhile the server moved A -> C. The server loads only the current state:
        // no historical snapshots are required because the ChangeSet carries its own before-state.
        var stateC = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1, Label = "b" }));
        var arrival = incoming.RebaseOnto(stateC);

        // !arrival.HasConflicts, so apply and save under the normal DB concurrency token.
        // Conflicts would instead return structured conflict information without saving.
        var saved = arrival.Patch.ToPatch().Apply(stateC);
        // saved.Value!.RetryCount.Value == 2
        // saved.Value!.Label.Value == "b"
        DocsCheck.Require(!arrival.HasConflicts, "end-to-end rebase has no conflicts");
        DocsCheck.Require(saved.Value!.RetryCount.Value == 2, "client edit applied");
        DocsCheck.Require(saved.Value!.Label.Value == "b", "server edit preserved");
        // /sample
    }
}

// sample: core-models
[SparseFragmentModel]
public partial class CounterSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}
// /sample

// sample: keyed-first-models
[SparseFragmentModel]
public partial class Fleet
{
    public List<Server> Servers { get; set; } = new();
}

public partial class Server
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}
// /sample

// sample: rebase-first-models
[SparseFragmentModel]
public partial class RebaseSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}
// /sample
