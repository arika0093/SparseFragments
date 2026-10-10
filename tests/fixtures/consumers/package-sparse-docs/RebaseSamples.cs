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
        VersionedStoreSave();
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
            """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted","value":null},"after":{"state":"value","value":"rotated"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"value","value":2}}]}"""
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

    private static void VersionedStoreSave()
    {
        // sample: rebase-server-save
        var store = new RebaseVersionedStore();
        store.Seed(new RebaseDocsSettings { RetryCount = 1, Label = "a" }, version: 7);

        // The client read A at v7 and edited A -> B. The v7 token travels as request
        // metadata; the server never rejects a disjoint change on the stale client
        // token before reconciliation. Only the loaded server token guards the write.
        var clientBaseline = new RebaseDocsSettings { RetryCount = 1, Label = "a" };
        const int clientVersion = 7;
        var clientEdited = new RebaseDocsSettings { RetryCount = 2, Label = "a" };
        var incoming = JsonSerializer.Deserialize<RebaseDocsSettings.ChangePayload>(
            JsonSerializer.Serialize(clientBaseline.CreateChangeSet(clientEdited).ToPayload())
        )!.ToChangeSet();

        // Another writer persisted C at v8 (Label "a" -> "b") before the save.
        var writerRow = store.LoadCurrent(out var writerVersion);
        if (!store.TrySave(writerVersion, new RebaseDocsSettings { RetryCount = 1, Label = "b" }))
        {
            throw new InvalidOperationException("The seed write conflicts.");
        }

        // Disjoint success: the server loads only C at v8, rebases B onto it, and
        // conditionally writes against v8. Rebase alone never commits the row.
        var loaded = store.LoadCurrent(out var loadedVersion);
        // loadedVersion == 8
        if (!incoming.TryApplyTo(loaded, out var merged, out var saveConflicts))
        {
            throw new InvalidOperationException("The change conflicts with the current row.");
        }

        if (!store.TrySave(loadedVersion, merged))
        {
            throw new InvalidOperationException("The row moved under the save; reload and retry.");
        }

        // The row now holds RetryCount 2 with Label "b" at v9. The client token is
        // still 7: it described the read, never the write guard.

        // An overlapping edit reports structured conflicts and writes nothing: with
        // C also moving RetryCount, the same incoming change cannot rebase.
        var overlapCurrent = new RebaseDocsSettings { RetryCount = 3, Label = "b" };
        if (incoming.TryApplyTo(overlapCurrent, out _, out var overlapConflicts))
        {
            throw new InvalidOperationException("Expected a conflict.");
        }

        var overlapConflict = overlapConflicts.Single();
        // overlapConflict.Kind == SparseConflictKind.Scalar
        // overlapConflict.PathText == "RetryCount"

        // A race after the read but before the conditional write fails the
        // affected-row check instead of overwriting. The save reloads the newer row,
        // rebases the same incoming change onto it, and retries with a bound instead
        // of reusing the stale merged model.
        var raced = store.LoadCurrent(out var racedVersion);
        // racedVersion == 9
        if (!incoming.TryApplyTo(raced, out var racedMerge, out _))
        {
            throw new InvalidOperationException("The change conflicts with the current row.");
        }

        store.TrySave(racedVersion, new RebaseDocsSettings { RetryCount = 2, Label = "c" });
        var raceHit = store.TrySave(racedVersion, racedMerge);
        // raceHit == false: the row moved under the save, so nothing overwrote it.

        var saved = raceHit;
        var attempts = 0;
        while (!saved && attempts < 2)
        {
            attempts++;
            var retryLoaded = store.LoadCurrent(out var retryVersion);
            if (!incoming.TryApplyTo(retryLoaded, out var retryMerged, out _))
            {
                throw new InvalidOperationException("The change conflicts with the current row.");
            }

            saved = store.TrySave(retryVersion, retryMerged);
        }

        if (!saved)
        {
            throw new InvalidOperationException("The save did not converge; report exhaustion.");
        }

        // The row holds RetryCount 2 with Label "c" at v11. A stale-token rejection, a
        // semantic conflict, an affected-row race with exhaustion, and a validation
        // failure stay distinct outcomes with distinct handling.
        // /sample
        DocsCheck.Require(clientVersion == 7, "client token records the read version");
        DocsCheck.Require(writerRow.RetryCount == 1, "writer loaded the read state");
        DocsCheck.Require(loadedVersion == 8, "server rebases onto the loaded token");
        DocsCheck.Require(
            saveConflicts is null,
            "conflict-free rebase reports no conflicts"
        );
        DocsCheck.Require(
            merged.RetryCount == 2 && merged.Label == "b",
            "disjoint rebase keeps the client edit and the server edit"
        );
        DocsCheck.Require(
            overlapConflict.Kind == SparseConflictKind.Scalar,
            "overlap conflict kind is Scalar"
        );
        DocsCheck.Require(
            overlapConflict.PathText == "RetryCount",
            "overlap conflict names the member"
        );
        DocsCheck.Require(racedVersion == 9, "race starts from the saved token");
        DocsCheck.Require(!raceHit, "stale conditional write fails without overwriting");
        DocsCheck.Require(saved && attempts == 1, "bounded retry converges on the reload");
        var finalRow = store.LoadCurrent(out var finalVersion);
        DocsCheck.Require(
            finalRow.RetryCount == 2 && finalRow.Label == "c",
            "retried save keeps the client edit over the newer row"
        );
        DocsCheck.Require(finalVersion == 11, "versions advance monotonically");
        DocsCheck.Require(store.WriteCount == 4, "conflict and race paths write nothing extra");
    }
}

[SparseFragmentModel]
public partial class RebaseDocsSettings
{
    public int RetryCount { get; set; }

    public string? Label { get; set; }
}

// sample: rebase-server-store
sealed class RebaseVersionedStore
{
    private RebaseDocsSettings _row = new() { RetryCount = 1, Label = "a" };
    private int _version = 7;
    private bool _seeded;

    public int WriteCount { get; private set; }

    public void Seed(RebaseDocsSettings row, int version)
    {
        _row = row;
        _version = version;
        _seeded = true;
    }

    public RebaseDocsSettings LoadCurrent(out int version)
    {
        version = _version;
        return new RebaseDocsSettings { RetryCount = _row.RetryCount, Label = _row.Label };
    }

    // Compare-and-swap: the write lands only when the row still carries
    // expectedVersion, and the store owns the increment. A false return
    // means another writer moved the row first.
    public bool TrySave(int expectedVersion, RebaseDocsSettings merged)
    {
        if (!_seeded || expectedVersion != _version)
        {
            return false;
        }

        _row = new RebaseDocsSettings { RetryCount = merged.RetryCount, Label = merged.Label };
        _version++;
        WriteCount++;
        return true;
    }
}
// /sample
