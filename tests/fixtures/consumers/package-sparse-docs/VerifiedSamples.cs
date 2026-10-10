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
        CoreBuilder();
        CorePatch();
        CoreBetween();
        CoreChangeSet();
        CoreTyped();
        CoreNested();
        CoreAlgebra();
        CoreSerialization();
        CoreChangePayload();
        KeyedFirst();
        KeyedTyped();
        KeyedUnassignedFlow();
        RebaseFirst();
        RebaseApplied();
        RebaseConflict();
        RebasePresence();
        RebasePolicy();
        RebaseRedacted();
        RebaseEndToEnd();
    }

    private static void CoreCreate()
    {
        // sample: core-create
        var current = CounterSettings.Fragment.From(
            new CounterSettings { Label = "a", RetryCount = 1 }
        );

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

    private static void CoreBuilder()
    {
        // sample: core-builder
        var original = new CounterSettings.Fragment { Label = "before" };

        var builder = original.ToBuilder();
        builder.Label = Optional<string?>.Present(null);

        var result = builder.Build();
        // result.Label.IsPresent == true, value null; original.Label stays "before"

        builder.Label = Optional<string?>.Missing;
        // builder.Build().Label.IsPresent == false; result still carries present null
        DocsCheck.Require(
            result.Label.IsPresent && result.Label.Value is null,
            "built present null"
        );
        DocsCheck.Require(original.Label.Value == "before", "original untouched by the builder");
        DocsCheck.Require(!builder.Build().Label.IsPresent, "builder tracks missing");
        DocsCheck.Require(result.Label.IsPresent, "earlier build keeps present null");
        // /sample
    }

    private static void CorePatch()
    {
        // sample: core-patch
        var basis = CounterSettings.Fragment.From(
            new CounterSettings { Label = "a", RetryCount = 1 }
        );

        var update = new CounterSettings.Patch { Label = (string?)null };
        var updated = basis.Apply(update);
        // updated.Label.IsPresent == true
        // updated.Label.Value is null
        // updated.RetryCount.Value == 1
        DocsCheck.Require(updated.Label.IsPresent, "explicit null stays present");
        DocsCheck.Require(updated.Label.Value is null, "value is null");
        DocsCheck.Require(updated.RetryCount.Value == 1, "untouched member kept");

        var remove = new CounterSettings.Patch();
        remove.RetryCount.Remove();
        // !remove.Apply(basis).Value!.RetryCount.IsPresent
        DocsCheck.Require(
            !remove.Apply(basis).Value!.RetryCount.IsPresent,
            "Remove drops the contribution"
        );
        // /sample
    }

    private static void CoreBetween()
    {
        // sample: core-between
        var a = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a" })
        );
        var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

        var removal = CounterSettings.ChangeSet.Between(a, b); // Label: present → missing
        // !removal.ToPatch().Apply(a).Value!.Label.IsPresent
        DocsCheck.Require(
            !removal.ToPatch().Apply(a).Value!.Label.IsPresent,
            "Between preserves the removal"
        );
        // /sample
    }

    private static void CoreChangeSet()
    {
        // sample: core-changeset
        var start = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
        );
        var finish = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
        );

        // Patch says "set these values": mutable and baseline-free.
        var desired = new CounterSettings.Patch { Label = "b" };

        // ChangeSet says "these values changed from X to Y": immutable and baseline-aware.
        var transition = CounterSettings.ChangeSet.Between(start, finish);
        // transition.IsEmpty == false
        // transition.ToPatch().Apply(start) replays finish
        DocsCheck.Require(!transition.IsEmpty, "ChangeSet carries the Label change");
        DocsCheck.Require(
            CounterSettings.Patch.Between(transition.ToPatch().Apply(start), finish).IsEmpty,
            "ToPatch replays the transition"
        );

        // ChangeSet.FromPatch attaches a known baseline to an existing patch.
        var fromPatch = CounterSettings.ChangeSet.FromPatch(start, desired);
        // fromPatch.ToPatch().Apply(start) replays finish
        DocsCheck.Require(
            CounterSettings.Patch.Between(fromPatch.ToPatch().Apply(start), finish).IsEmpty,
            "FromPatch derives the same transition"
        );
        // /sample
    }

    private static void CoreTyped()
    {
        // sample: core-typed
        var before = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
        );
        var after = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
        );

        var changes = CounterSettings.ChangeSet.Between(before, after);

        if (changes.Label.IsChanged)
        {
            Console.WriteLine($"{changes.Label.Before} -> {changes.Label.After}");
        }
        // changes.Label.Before.Value == "a"
        // changes.Label.After.Value == "b"
        // changes.RetryCount.IsChanged == false
        DocsCheck.Require(changes.Label.IsChanged, "changed member reports IsChanged");
        DocsCheck.Require(changes.Label.Before.Value == "a", "Before preserves the old value");
        DocsCheck.Require(changes.Label.After.Value == "b", "After preserves the new value");
        DocsCheck.Require(!changes.RetryCount.IsChanged, "unchanged member stays typed");
        DocsCheck.Require(!changes.RetryCount.Before.IsPresent, "unchanged Before stays missing");
        // /sample
    }

    private static void CoreNested()
    {
        // sample: core-nested
        var before = Optional<DocsOrder.Fragment?>.Present(
            DocsOrder.Fragment.From(
                new DocsOrder
                {
                    Name = "a",
                    Customer = new DocsCustomer { Name = "Ann" },
                }
            )
        );
        var after = Optional<DocsOrder.Fragment?>.Present(
            DocsOrder.Fragment.From(
                new DocsOrder
                {
                    Name = "a",
                    Customer = new DocsCustomer { Name = "Bob" },
                }
            )
        );

        var changes = DocsOrder.ChangeSet.Between(before, after);
        // changes.Name.IsChanged == false
        // changes.Customer.Name.IsChanged == true
        // changes.Customer.Name.Before.Value == "Ann"
        // changes.Customer.Name.After.Value == "Bob"
        DocsCheck.Require(!changes.Name.IsChanged, "unchanged member reports IsChanged == false");
        DocsCheck.Require(!changes.Customer.IsEmpty, "nested transition is non-empty");
        DocsCheck.Require(changes.Customer.Name.IsChanged, "nested member change observed");
        DocsCheck.Require(changes.Customer.Name.Before.Value == "Ann", "nested Before preserved");
        DocsCheck.Require(changes.Customer.Name.After.Value == "Bob", "nested After preserved");
        // /sample
    }

    private static void CoreAlgebra()
    {
        // sample: core-algebra
        var s0 = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
        );
        var s1 = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
        );
        var s2 = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 })
        );

        // ChangeSet + ChangeSet -> ChangeSet for contiguous transitions.
        var first = CounterSettings.ChangeSet.Between(s0, s1);
        var second = CounterSettings.ChangeSet.Between(s1, s2);
        var combined = first.Compose(second);
        // combined.ToPatch().Apply(s0) reaches s2
        DocsCheck.Require(
            CounterSettings.Patch.Between(combined.ToPatch().Apply(s0), s2).IsEmpty,
            "composed ChangeSet spans both transitions"
        );

        // ChangeSet inverts without an external baseline.
        var undone = combined.Invert();
        // undone.ToPatch().Apply(s2) walks back to s0
        DocsCheck.Require(
            CounterSettings.Patch.Between(undone.ToPatch().Apply(s2), s0).IsEmpty,
            "inverted ChangeSet walks back"
        );

        // Patch + Patch -> Patch stays baseline-free.
        var local = new CounterSettings.Patch { Label = "x" };
        var more = new CounterSettings.Patch { RetryCount = 5 };
        var both = local.Compose(more);
        // both.Label == "x", both.RetryCount == 5
        DocsCheck.Require(
            both.Label.Value == "x" && both.RetryCount.Value == 5,
            "composed Patch carries both operations"
        );
        // /sample
    }

    private static void CoreSerialization()
    {
        // sample: core-serialization
        var start = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
        );
        var finish = Optional<CounterSettings.Fragment?>.Present(
            CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 })
        );

        var changes = CounterSettings.ChangeSet.Between(start, finish);

        var json = JsonSerializer.Serialize(changes.ToPayload());
        var restored = JsonSerializer
            .Deserialize<CounterSettings.ChangePayload>(json)!
            .ToChangeSet();
        // restored.ToPatch().Apply(start) replays finish
        DocsCheck.Require(
            CounterSettings.Patch.Between(restored.ToPatch().Apply(start), finish).IsEmpty,
            "deserialized ChangeSet replays the transition"
        );

        // /sample
    }

    private static void CoreChangePayload()
    {
        // sample: core-change-payload
        var current = LoginSettings.Fragment.From(
            new LoginSettings { DisplayName = "a", Password = "before-password" }
        );

        var rotation = new LoginSettings.Patch { Password = "after-password" };

        // A command needs no baseline: the envelope redacts what it never observed.
        var command = LoginSettings.ChangePayload.FromPatch(rotation);
        var applied = current.Apply(command.ToPatch());
        // applied.DisplayName.Value == "a"
        // applied.Password.Value == "after-password"
        DocsCheck.Require(applied.DisplayName.Value == "a", "untouched member kept");
        DocsCheck.Require(
            applied.Password.Value == "after-password",
            "baseline-free command applied"
        );

        var transition = LoginSettings.ChangeSet.Between(
            Optional<LoginSettings.Fragment?>.Present(current),
            Optional<LoginSettings.Fragment?>.Present(applied)
        );
        var wire = JsonSerializer.Serialize(transition.ToPayload());
        // wire carries "after-password" but never "before-password"
        DocsCheck.Require(!wire.Contains("before-password"), "before-state stays undisclosed");
        DocsCheck.Require(wire.Contains("after-password"), "desired state travels");
        // /sample
    }

    private static void KeyedFirst()
    {
        // sample: keyed-first
        var before = new Fleet
        {
            Servers = new()
            {
                new Server { Id = "a", Host = "old" },
            },
        };
        var after = new Fleet
        {
            Servers = new()
            {
                new Server { Id = "a", Host = "new" },
                new Server { Id = "b" },
            },
        };

        var changes = before.CreateChangeSet(after); // add/remove/edit by key
        if (!changes.TryApplyTo(before, out var applied))
        {
            throw new InvalidOperationException("The keyed changes conflict.");
        }

        // applied.Servers.Count == 2
        // applied.Servers.Single(s => s.Id == "a").Host == "new"
        DocsCheck.Require(applied.Servers.Count == 2, "added element present");
        DocsCheck.Require(applied.Servers.Single(s => s.Id == "a").Host == "new", "edit by key");
        // /sample
    }

    private static void KeyedTyped()
    {
        // sample: keyed-typed
        var before = new Fleet
        {
            Servers = new()
            {
                new Server { Id = "a", Host = "A" },
                new Server { Id = "b", Host = "B" },
            },
        };
        var after = new Fleet
        {
            Servers = new()
            {
                new Server { Id = "b", Host = "B2" },
                new Server { Id = "c", Host = "C" },
            },
        };

        var changes = before.CreateChangeSet(after);
        var servers = changes.Servers;
        // servers.Added.Single().Id == "c"
        // servers.Removed.Single().Id == "a"
        // servers.Edited["b"].Host.After.Value == "B2"
        // servers.BeforeOrder.SequenceEqual(["a", "b"])
        // servers.AfterOrder.SequenceEqual(["b", "c"])
        // servers.OrderChanged == true
        foreach (var item in servers)
        {
            if (item.IsEdited)
            {
                Console.WriteLine(item.Edit.Host.IsChanged);
            }
        }
        var edited = servers.GetChange("b");
        // edited.IsEdited == true
        // edited.Edit.Host.After.Value == "B2"
        DocsCheck.Require(
            servers.Added.Count == 1 && servers.Added.Single().Id == "c",
            "added projection"
        );
        DocsCheck.Require(
            servers.Removed.Count == 1 && servers.Removed.Single().Id == "a",
            "removed projection"
        );
        DocsCheck.Require(servers.Edited["b"].Host.After.Value == "B2", "edited projection");
        DocsCheck.Require(servers.BeforeOrder.SequenceEqual(["a", "b"]), "before order preserved");
        DocsCheck.Require(servers.AfterOrder.SequenceEqual(["b", "c"]), "after order preserved");
        DocsCheck.Require(servers.OrderChanged, "order change observed");
        DocsCheck.Require(edited.IsEdited, "keyed lookup observes the edit");
        DocsCheck.Require(
            edited.Edit.Host.After.Value == "B2",
            "keyed lookup carries the nested change"
        );
        DocsCheck.Require(servers.GetChange("absent").IsEmpty, "unknown key is empty");
        // /sample
    }

    private static void KeyedUnassignedFlow()
    {
        // sample: keyed-unassigned-flow
        var before = new PendingFleet { Servers = new() { new PendingServer { Id = 4 } } };
        var after = new PendingFleet
        {
            Servers = new()
            {
                new PendingServer { Id = 0, Host = "client-1" },
                new PendingServer { Id = 4, Host = "saved" },
                new PendingServer { Id = 0, Host = "client-2" },
            },
        };
        var changes = before.CreateChangeSet(after);
        // Send changes.ToPayload() to the server; the server inserts the rows,
        // assigns database IDs, and may normalize or reorder them.
        // Do not acknowledge the unassigned transition with AcceptChanges.
        // Replace with the authoritative state and start a fresh session:
        var persisted = new PendingFleet
        {
            Servers = new()
            {
                new PendingServer { Id = 11, Host = "client-1" },
                new PendingServer { Id = 4, Host = "saved" },
                new PendingServer { Id = 12, Host = "client-2" },
            },
        };
        var session = persisted.CreateEditSession();
        // session.HasChanges == false
        DocsCheck.Require(!session.HasChanges, "fresh session on authoritative state is clean");
        DocsCheck.Require(!changes.IsEmpty, "unassigned additions form a transition");
        // /sample
    }

    private static void RebaseFirst()
    {
        // sample: rebase-first
        var baseModel = new RebaseSettings { RetryCount = 1, Label = "a" };
        var editedModel = new RebaseSettings { RetryCount = 2, Label = "a" };
        var currentModel = new RebaseSettings { RetryCount = 1, Label = "b" };

        var changes = baseModel.CreateChangeSet(editedModel);
        if (!changes.TryApplyTo(currentModel, out var reconciled))
        {
            throw new InvalidOperationException("The change conflicts with the current model.");
        }

        // reconciled.RetryCount == 2
        // reconciled.Label == "b"
        DocsCheck.Require(reconciled.RetryCount == 2, "local edit kept");
        DocsCheck.Require(reconciled.Label == "b", "concurrent edit kept");
        // /sample
    }

    private static void RebaseApplied()
    {
        // sample: rebase-applied
        var appliedBase = new RebaseSettings { RetryCount = 1 };
        var appliedEdited = new RebaseSettings { RetryCount = 2 };
        var alreadyThere = new RebaseSettings { RetryCount = 2 };

        // Current == After: the change is already present, so rebase is a no-op.
        var noOp = appliedBase.CreateChangeSet(appliedEdited);
        if (!noOp.TryApplyTo(alreadyThere, out var unchanged))
        {
            throw new InvalidOperationException("The change conflicts with the current model.");
        }

        // unchanged.RetryCount == 2
        DocsCheck.Require(unchanged.RetryCount == 2, "already-applied rebase is a no-op");
        // /sample
    }

    private static void RebaseConflict()
    {
        // sample: rebase-conflict
        var conflictBase = new RebaseSettings { RetryCount = 1 };
        var conflictEdited = new RebaseSettings { RetryCount = 2 };
        var conflictCurrent = new RebaseSettings { RetryCount = 3 };

        if (
            conflictBase
                .CreateChangeSet(conflictEdited)
                .TryApplyTo(conflictCurrent, out _, out var conflicts)
        )
        {
            throw new InvalidOperationException("Expected a conflict.");
        }

        var conflict = conflicts.Single();
        // conflict.Kind == SparseConflictKind.Scalar
        // conflict.PathText == "RetryCount"
        DocsCheck.Require(conflict.Kind == SparseConflictKind.Scalar, "conflict kind is Scalar");
        DocsCheck.Require(conflict.PathText == "RetryCount", "conflict path names the member");
        DocsCheck.Require(
            Equals(conflict.BaseValue.Value, 1)
                && Equals(conflict.LocalValue.Value, 2)
                && Equals(conflict.CurrentValue.Value, 3),
            "conflict carries base/local/current values"
        );
        // /sample
    }

    private static void RebasePresence()
    {
        // sample: rebase-presence
        var missing = Optional<RebaseSettings.Fragment?>.Missing;
        var presentNull = Optional<RebaseSettings.Fragment?>.Present(null);
        var rootChange = RebaseSettings.ChangeSet.Between(missing, presentNull);
        RebaseResult<RebaseSettings.ChangeSet> result = rootChange.RebaseOnto(missing);

        if (result.HasConflicts)
        {
            throw new InvalidOperationException("The root transition conflicts.");
        }

        var applied = result.Rebased.ToPatch().Apply(missing);
        // applied.IsPresent && applied.Value is null
        DocsCheck.Require(applied.IsPresent && applied.Value is null, "root presence preserved");
        // /sample
    }

    private static void RebaseEndToEnd()
    {
        // sample: rebase-e2e
        // Server sends DTO (state A); the client edits A -> B and creates a ChangeSet.
        var stateA = new RebaseSettings { RetryCount = 1, Label = "a" };
        var stateB = new RebaseSettings { RetryCount = 2, Label = "a" };
        var outgoing = stateA.CreateChangeSet(stateB);

        // The typed payload travels as JSON through the application's own transport.
        var json = JsonSerializer.Serialize(outgoing.ToPayload());
        var incoming = JsonSerializer
            .Deserialize<RebaseSettings.ChangePayload>(json)!
            .ToChangeSet();

        // Meanwhile the server moved A -> C. The server loads only the current state:
        // no historical snapshots are required because the ChangeSet carries its own before-state.
        var stateC = new RebaseSettings { RetryCount = 1, Label = "b" };

        if (incoming.TryApplyTo(stateC, out var saved, out var conflicts))
        {
            // Save under the normal DB concurrency token.
            // saved.RetryCount == 2
            // saved.Label == "b"
            DocsCheck.Require(saved.RetryCount == 2, "client edit applied");
            DocsCheck.Require(saved.Label == "b", "server edit preserved");
        }
        else
        {
            // Surface conflicts without saving a partially applied model.
            // conflicts contains paths, kinds, and base/local/current values.
            DocsCheck.Require(conflicts.Count > 0, "structured conflicts returned");
        }
        // /sample
    }

    private static void RebasePolicy()
    {
        // sample: rebase-policy
        var policyBase = new RebasePolicySettings { Label = "a", Tag = "a" };
        var policyEdited = new RebasePolicySettings { Label = "b", Tag = "b" };
        var policyCurrent = new RebasePolicySettings { Label = "a", Tag = "c" };

        if (!policyBase.CreateChangeSet(policyEdited).TryApplyTo(policyCurrent, out var merged))
        {
            throw new InvalidOperationException("The policy reconciles divergent labels.");
        }

        // merged.Label == "b"
        // merged.Tag == "b|c"
        DocsCheck.Require(merged.Label == "b", "plain member replays");
        DocsCheck.Require(merged.Tag == "b|c", "policy merges divergent labels");
        // /sample
    }

    private static void RebaseRedacted()
    {
        // sample: rebase-redacted
        var secretBase = new RebaseSettings { RetryCount = 1, Label = "a" };
        var secretEdited = new RebaseSettings { RetryCount = 1, Label = "new-secret" };
        var secretCurrent = new RebaseSettings { RetryCount = 1, Label = "other" };
        var redacted = new ChangePayloadRebaseOptions
        {
            RejectChangesWithRedactedBeforeValuesDuringRebase = true,
            RedactedBeforePaths = ["Label"],
        };

        if (
            secretBase
                .CreateChangeSet(secretEdited)
                .TryApplyTo(secretCurrent, out _, out var redactedConflicts, redacted)
        )
        {
            throw new InvalidOperationException("Expected a redacted-before failure.");
        }

        var redactedConflict = redactedConflicts.Single();
        // redactedConflict.Kind == SparseConflictKind.RedactedBefore
        // redactedConflict.PathText == "Label"
        DocsCheck.Require(
            redactedConflict.Kind == SparseConflictKind.RedactedBefore,
            "redacted-before failure is typed"
        );
        DocsCheck.Require(redactedConflict.PathText == "Label", "conflict names the member");
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

// sample: core-change-payload-models
[SparseFragmentModel]
public partial class LoginSettings
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
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

// sample: keyed-unassigned-model
[SparseFragmentModel]
public partial class PendingFleet
{
    public List<PendingServer> Servers { get; set; } = new();
}

public partial class PendingServer
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Host { get; set; } = string.Empty;
}

// /sample

// sample: core-nested-models
[SparseFragmentModel]
public partial class DocsOrder
{
    public string? Name { get; set; }

    public DocsCustomer? Customer { get; set; }
}

public partial class DocsCustomer
{
    public string Name { get; set; } = string.Empty;
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

// sample: rebase-policy-models
[SparseFragmentModel]
public partial class RebasePolicySettings
{
    public string? Label { get; set; }

    [SparseRebasePolicy(typeof(ConcatLabelPolicy))]
    public string? Tag { get; set; }
}

public sealed class ConcatLabelPolicy : FragmentRebasePolicy<string?>
{
    public override bool AreEqual(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    public override bool TryRebase(
        Optional<string?> editBase,
        Optional<string?> desired,
        Optional<string?> current,
        out Optional<string?> rebased,
        out string? reason
    )
    {
        if (!editBase.IsPresent || !desired.IsPresent || !current.IsPresent)
        {
            return FragmentRebasePolicy<string?>
                .FailOnConflict()
                .TryRebase(editBase, desired, current, out rebased, out reason);
        }

        if (AreEqual(desired.Value, editBase.Value))
        {
            rebased = current;
            reason = null;
            return true;
        }

        if (AreEqual(current.Value, editBase.Value) || AreEqual(current.Value, desired.Value))
        {
            rebased = desired;
            reason = null;
            return true;
        }

        rebased = Optional<string?>.Present(desired.Value + "|" + current.Value);
        reason = null;
        return true;
    }
}
// /sample
