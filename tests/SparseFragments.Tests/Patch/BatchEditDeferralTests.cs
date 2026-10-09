using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>BatchEdit must defer intermediate snapshot/diff work (#133).</summary>
public sealed class BatchEditDeferralTests
{
    private sealed class Instrumented
    {
        public int Snapshots;
        public int Diffs;
        public List<NeutralSessionModel.ChangeSet> Transitions = [];
    }

    private static EditSessionCore<
        NeutralSessionModel,
        NeutralSessionModel.Fragment,
        NeutralSessionModel.Patch,
        NeutralSessionModel.ChangeSet,
        NeutralObservable,
        NeutralView
    > CreateSession(NeutralSessionModel model, Instrumented probe)
    {
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
                probe.Snapshots++;
                return NeutralSessionModel.Fragment.From(current);
            },
            Between = (before, after) =>
            {
                probe.Diffs++;
                return NeutralSessionModel.ChangeSet.Between(before, after);
            },
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
        session.TransitionObserved += probe.Transitions.Add;
        return session;
    }

    [Test]
    public void BatchPublishesOneNetTransitionWithConstantDiffCost()
    {
        const int edits = 50;
        var batchedProbe = new Instrumented();
        var batched = CreateSession(new NeutralSessionModel(), batchedProbe);
        batchedProbe.Snapshots = 0;
        batchedProbe.Diffs = 0;

        batched.BatchEdit(() =>
        {
            for (var index = 0; index < edits; index++)
            {
                batched.Observable.Name = "value-" + index;
                batched.Observable.Tags.Add("tag-" + index);
            }
        });

        batchedProbe.Transitions.Count.ShouldBe(1);
        batched.HasChanges.ShouldBeTrue();
        // One exit snapshot plus the net-transition and cache diffs, regardless of N.
        batchedProbe.Snapshots.ShouldBeLessThanOrEqualTo(2);
        batchedProbe.Diffs.ShouldBeLessThanOrEqualTo(4);

        var singleProbe = new Instrumented();
        var single = CreateSession(new NeutralSessionModel(), singleProbe);
        singleProbe.Snapshots = 0;
        singleProbe.Diffs = 0;

        for (var index = 0; index < edits; index++)
        {
            single.Observable.Name = "value-" + index;
            single.Observable.Tags.Add("tag-" + index);
        }

        // Without batching every mutation pays for a full snapshot and two diffs.
        singleProbe.Snapshots.ShouldBe(edits * 2);
        singleProbe.Diffs.ShouldBe(edits * 4);
        singleProbe.Transitions.Count.ShouldBe(edits * 2);
    }

    [Test]
    public void BatchEditThenRevertPublishesNoTransition()
    {
        var probe = new Instrumented();
        var session = CreateSession(new NeutralSessionModel { Name = "base" }, probe);
        var notifications = 0;
        session.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(session.HasChanges))
            {
                notifications++;
            }
        };

        session.BatchEdit(() =>
        {
            session.Observable.Name = "temporary";
            session.Observable.Child!.Value = "nested";
            session.Observable.Name = "base";
            session.Observable.Child!.Value = string.Empty;
        });

        probe.Transitions.ShouldBeEmpty();
        session.HasChanges.ShouldBeFalse();
        notifications.ShouldBe(1);
    }

    [Test]
    public void BatchStillPublishesNetTransitionWhenEditThrows()
    {
        var probe = new Instrumented();
        var session = CreateSession(new NeutralSessionModel { Name = "base" }, probe);

        Should.Throw<InvalidOperationException>(() =>
            session.BatchEdit(() =>
            {
                session.Observable.Name = "after";
                throw new InvalidOperationException("edit failed");
            })
        );

        probe.Transitions.Count.ShouldBe(1);
        probe.Transitions[0].Name.After.Value.ShouldBe("after");
        session.HasChanges.ShouldBeTrue();
    }
}
