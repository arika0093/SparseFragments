using System.Text.Json;
using SparseFragments;

// Canonical compile-checked mirror of docs/rebase.md.
// Covers the ChangeSet model: disjoint edits replaying cleanly onto the
// current state, already-applied edits becoming semantic no-ops, and
// conflicting edits producing structured SparseConflict values.
public static class RebaseSamples
{
    public static void Run()
    {
        DisjointEditsReplayCleanly();
        AlreadyAppliedEditsBecomeNoOps();
        ConflictingEditsProduceStructuredConflicts();
        PresenceAwareRootStateRebases();
        MixedRequestsApplyBlindSetsAtomically();
        InPlaceApplyChecksBeforeState();
    }

    private static void DisjointEditsReplayCleanly()
    {
        var baseModel = new RebaseDocsSettings { RetryCount = 1, Label = "a" };
        var editedModel = new RebaseDocsSettings { RetryCount = 2, Label = "a" };
        var currentModel = new RebaseDocsSettings { RetryCount = 1, Label = "b" };
        var changes = baseModel.CreateChangeSet(editedModel);
        if (!changes.TryApplyTo(currentModel, out var applied))
        {
            throw new InvalidOperationException("Expected a conflict-free rebase.");
        }
        DocsCheck.Require(
            applied.RetryCount == 2 && applied.Label == "b",
            "disjoint rebase replays local edit and keeps concurrent edit"
        );
    }

    private static void AlreadyAppliedEditsBecomeNoOps()
    {
        var baseModel = new RebaseDocsSettings { RetryCount = 1 };
        var editedModel = new RebaseDocsSettings { RetryCount = 2 };
        var alreadyThere = new RebaseDocsSettings { RetryCount = 2 };
        var noOp = baseModel.CreateChangeSet(editedModel);
        if (!noOp.TryApplyTo(alreadyThere, out var applied))
        {
            throw new InvalidOperationException("The change conflicts with the current model.");
        }
        DocsCheck.Require(
            applied.RetryCount == 2,
            "already-applied rebase keeps the current value"
        );
    }

    private static void ConflictingEditsProduceStructuredConflicts()
    {
        var baseModel = new RebaseDocsSettings { RetryCount = 1 };
        var editedModel = new RebaseDocsSettings { RetryCount = 2 };
        var currentModel = new RebaseDocsSettings { RetryCount = 3 };
        if (
            baseModel
                .CreateChangeSet(editedModel)
                .TryApplyTo(currentModel, out _, out var conflicts)
        )
        {
            throw new InvalidOperationException("Expected a conflict.");
        }
        DocsCheck.Require(conflicts.Count == 1, "one structured conflict");
        var conflict = conflicts.Single();
        DocsCheck.Require(conflict.Kind == SparseConflictKind.Scalar, "conflict kind is Scalar");
        DocsCheck.Require(conflict.PathText == "RetryCount", "conflict path names the member");
        DocsCheck.Require(
            Equals(conflict.BaseValue.Value, 1)
                && Equals(conflict.LocalValue.Value, 2)
                && Equals(conflict.CurrentValue.Value, 3),
            "conflict carries base/local/current values"
        );
    }

    private static void PresenceAwareRootStateRebases()
    {
        var missing = Optional<RebaseDocsSettings.Fragment?>.Missing;
        var presentNull = Optional<RebaseDocsSettings.Fragment?>.Present(null);
        var rootChange = RebaseDocsSettings.ChangeSet.Between(missing, presentNull);
        RebaseResult<RebaseDocsSettings.ChangeSet> result = rootChange.RebaseOnto(missing);
        DocsCheck.Require(!result.HasConflicts, "missing-to-null root transition rebases");
        var applied = result.Rebased.ToPatch().Apply(missing);
        DocsCheck.Require(
            applied.IsPresent && applied.Value is null,
            "root rebase preserves present-null state"
        );
    }

    private static void MixedRequestsApplyBlindSetsAtomically()
    {
        // sample: mixed-apply
        var currentModel = new RebaseDocsSettings { RetryCount = 1, Label = "current" };
        var payload = JsonSerializer.Deserialize<RebaseDocsSettings.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted"},"after":{"state":"value","value":"rotated"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"value","value":2}}]}"""
        )!;
        if (!payload.TryApplyMixedTo(currentModel, out var updated, out var outcome))
        {
            throw new InvalidOperationException("The change conflicts with the current model.");
        }

        // updated.Label == "rotated"
        // updated.RetryCount == 2
        // /sample
        DocsCheck.Require(
            updated.Label == "rotated" && updated.RetryCount == 2,
            "mixed request applies the blind set and the rebased transition"
        );
        DocsCheck.Require(
            outcome.WriteOnlyPaths.Count == 1 && outcome.WriteOnlyPaths[0] == "Label",
            "mixed outcome names the write-only path"
        );
    }

    private static void InPlaceApplyChecksBeforeState()
    {
        // sample: rebase-in-place
        var inPlaceBase = new RebaseDocsSettings { RetryCount = 1, Label = "a" };
        var inPlaceEdited = new RebaseDocsSettings { RetryCount = 2, Label = "a" };
        var boundModel = new RebaseDocsSettings { RetryCount = 1, Label = "b" };

        var pending = inPlaceBase.CreateChangeSet(inPlaceEdited);
        if (!pending.TryApplyInPlace(boundModel, out var inPlaceConflicts))
        {
            throw new InvalidOperationException("The change conflicts with the bound model.");
        }

        // boundModel.RetryCount == 2
        // boundModel.Label == "b"
        // /sample
        DocsCheck.Require(
            inPlaceConflicts is null,
            "conflict-free in-place apply reports no conflicts"
        );
        DocsCheck.Require(
            boundModel.RetryCount == 2 && boundModel.Label == "b",
            "in-place apply replays the edit and keeps the concurrent edit"
        );
    }
}

[SparseFragmentModel]
public partial class RebaseDocsSettings
{
    public int RetryCount { get; set; }

    public string? Label { get; set; }
}
