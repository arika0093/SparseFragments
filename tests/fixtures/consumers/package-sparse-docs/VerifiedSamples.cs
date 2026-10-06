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
        KeyedFirst();
        RebaseFirst();
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
        DocsCheck.Require(!diff.Label.IsPresent, "unchanged Label is missing");
        DocsCheck.Require(diff.RetryCount.Value == 2, "changed RetryCount is present");

        var restored = CounterSettings.Fragment.From(beforeModel).ApplyChanges(diff);
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
        DocsCheck.Require(updated.Label.IsPresent, "explicit null stays present");
        DocsCheck.Require(updated.Label.Value is null, "value is null");
        DocsCheck.Require(updated.RetryCount.Value == 1, "untouched member kept");

        var remove = new CounterSettings.Patch();
        remove.RetryCount.Unset();
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

        var removal = CounterSettings.Patch.Between(a, b); // Label: present → missing
        DocsCheck.Require(
            !removal.Apply(a).Value!.Label.IsPresent, "Between preserves the removal");
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

        var patch = Fleet.Patch.Between(before, after); // add/remove/edit by key
        var applied = patch.Apply(before);              // original untouched

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
        var localPatch = new RebaseSettings.Patch { RetryCount = 2 };
        var currentState = Optional<RebaseSettings.Fragment?>.Present(
            RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1, Label = "b" }));

        var rebased = RebaseSettings.Patch.Rebase(baseState, localPatch, currentState);

        DocsCheck.Require(!rebased.HasConflicts, "disjoint edits replay cleanly");
        var reconciled = rebased.Patch.Apply(currentState);
        DocsCheck.Require(reconciled.Value!.RetryCount.Value == 2, "local edit kept");
        DocsCheck.Require(reconciled.Value!.Label.Value == "b", "concurrent edit kept");
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
