using SparseFragments;

// Canonical compile-checked mirror of docs/rebase.md.
// Covers the ChangeSet model: disjoint edits replaying cleanly onto the
// current state, already-applied edits becoming semantic no-ops, and
// conflicting edits producing structured SparsePatchConflict values.
public static class RebaseSamples
{
    public static void Run()
    {
        DisjointEditsReplayCleanly();
        AlreadyAppliedEditsBecomeNoOps();
        ConflictingEditsProduceStructuredConflicts();
    }

    private static void DisjointEditsReplayCleanly()
    {
        var baseState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 1, Label = "a" }));
        var editedState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 2, Label = "a" }));
        var currentState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 1, Label = "b" }));

        var changes = RebaseDocsSettings.ChangeSet.Between(baseState, editedState);
        RebaseResult<RebaseDocsSettings.ChangeSet> result = changes.RebaseOnto(currentState);
        DocsCheck.Require(!result.HasConflicts, "disjoint rebase has no conflicts");
        var applied = result.Patch.ToPatch().Apply(currentState);
        DocsCheck.Require(
            applied.Value!.RetryCount.Value == 2 && applied.Value.Label.Value == "b",
            "disjoint rebase replays local edit and keeps concurrent edit");
    }

    private static void AlreadyAppliedEditsBecomeNoOps()
    {
        var baseState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 1 }));
        var editedState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 2 }));

        var result = RebaseDocsSettings.ChangeSet.Between(baseState, editedState)
            .RebaseOnto(editedState);
        DocsCheck.Require(!result.HasConflicts, "already-applied rebase has no conflicts");
        DocsCheck.Require(result.Patch.IsEmpty, "already-applied rebase is a semantic no-op");
    }

    private static void ConflictingEditsProduceStructuredConflicts()
    {
        var baseState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 1 }));
        var editedState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 2 }));
        var currentState = Optional<RebaseDocsSettings.Fragment?>.Present(
            RebaseDocsSettings.Fragment.From(new RebaseDocsSettings { RetryCount = 3 }));

        var result = RebaseDocsSettings.ChangeSet.Between(baseState, editedState)
            .RebaseOnto(currentState);
        DocsCheck.Require(result.HasConflicts, "divergent edits conflict");
        DocsCheck.Require(result.Conflicts.Count == 1, "one structured conflict");
        var conflict = result.Conflicts.Single();
        DocsCheck.Require(
            conflict.Kind == SparsePatchConflictKind.Scalar,
            "conflict kind is Scalar");
        DocsCheck.Require(
            conflict.Path.SequenceEqual(new[] { "RetryCount" }),
            "conflict path names the member");
        DocsCheck.Require(
            Equals(conflict.BaseValue.Value, 1)
                && Equals(conflict.LocalValue.Value, 2)
                && Equals(conflict.CurrentValue.Value, 3),
            "conflict carries base/local/current values");
    }
}

[SparseFragmentModel]
public partial class RebaseDocsSettings
{
    public int RetryCount { get; set; }

    public string? Label { get; set; }
}
