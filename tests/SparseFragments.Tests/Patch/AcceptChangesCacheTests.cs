using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>Regression tests for AcceptChanges preserving the HasChanges cache (#122).</summary>
public sealed class AcceptChangesCacheTests
{
    [Test]
    public void AcceptChangesKeepsObservableCacheUntilRawModelIsExposed()
    {
        var model = new NeutralSessionModel();
        var snapshots = 0;
        var configuration = new EditSessionCoreConfiguration<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralObservable,
            NeutralView
        >
        {
            FromModel = current =>
            {
                snapshots++;
                return NeutralSessionModel.Fragment.From(current);
            },
            Between = NeutralSessionModel.ChangeSet.Between,
            ToPatch = static changes => changes.ToPatch(),
            IsEmpty = static changes => changes.IsEmpty,
            AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline),
            ToObservable = static (current, changed, access) =>
                new NeutralObservable(current, changed, access),
            ToCurrent = static current => new NeutralView(current),
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            RefreshObservable = static observable => observable.__SparseRefresh(),
        };
        var session = EditSessionCore<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralObservable,
            NeutralView
        >.Create(model, configuration);

        session.Observable.Name = "first edit";
        var afterFirstEdit = snapshots;

        // Sanity: observable-only edits populate the cache.
        session.HasChanges.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        snapshots.ShouldBe(afterFirstEdit);

        session.AcceptChanges();
        session.HasChanges.ShouldBeFalse();

        session.Observable.Name = "second edit";
        session.Observable.Tags.Add("nested");
        var afterSecondEdit = snapshots;

        // The save acknowledgement must not have disabled caching.
        session.HasChanges.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        snapshots.ShouldBe(afterSecondEdit);

        // External raw-model reads still opt out of the cache.
        _ = session.Model;
        session.HasChanges.ShouldBeTrue();
        snapshots.ShouldBe(afterSecondEdit + 1);
    }
}
