using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>Collection-view Model access must not bypass raw-model safeguards (#145).</summary>
public sealed class ObservableModelGuardTests
{
    [Test]
    public void RawListMutationAfterCachedReadIsVisible()
    {
        var model = new NeutralSessionModel { Tags = ["a"] };
        var session = model.CreateEditSession();
        session.Observable.Tags.Add("b");
        session.Observable.Tags.Remove("b");
        session.HasChanges.ShouldBeFalse();

        session.Observable.Tags.Model.Add("raw");

        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Tags.After.Value.ShouldBe(["a", "raw"]);
    }

    [Test]
    public void RawDictionaryMutationAfterCachedReadIsVisible()
    {
        var model = new NeutralSessionModel { Counts = new() { ["a"] = 1 } };
        var session = model.CreateEditSession();
        session.Observable.Counts.Add("b", 2);
        session.Observable.Counts.Remove("b");
        session.HasChanges.ShouldBeFalse();

        session.Observable.Counts.Model["raw"] = 3;

        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Counts.Added["raw"].ShouldBe(3);
    }

    [Test]
    public void ObservableOnlyCollectionEditsRetainTheCache()
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

        // Touch the views without reading Model: caching must survive.
        _ = session.Observable.Tags.Count;
        _ = session.Observable.Counts.Count;
        session.Observable.Tags.Add("observable");
        session.Observable.Counts.Add("observable", 1);
        var afterEdits = snapshots;

        session.HasChanges.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        snapshots.ShouldBe(afterEdits);
    }

    [Test]
    public void DescriptorSetterAcceptsSessionViewsWithoutFalseInvalidation()
    {
        var model = new NeutralSessionModel { Tags = ["a"] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(NeutralSessionModel.Tags), out var tags).ShouldBeTrue();
        tags.TrySetValue(session.Observable.Tags).ShouldBeTrue();

        model.Tags.ShouldBe(["a"]);
        session.HasChanges.ShouldBeFalse();

        session.Observable.ReplaceTags(["b"]);
        session.HasChanges.ShouldBeTrue();
        model.Tags.ShouldBe(["b"]);

        session.RevertChanges();
        session.HasChanges.ShouldBeFalse();
        model.Tags.ShouldBe(["a"]);
    }
}
