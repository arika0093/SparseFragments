using SparseTypes.Generated;
using Session = SparseTypes.Generated.EditSessionCore<
    SparseTypes.Tests.NeutralSessionModel,
    SparseTypes.Tests.NeutralSessionModel.Fragment,
    SparseTypes.Tests.NeutralSessionModel.Patch,
    SparseTypes.Tests.NeutralSessionModel.ChangeSet,
    SparseTypes.Tests.NeutralSessionModel.Observable,
    SparseTypes.Tests.NeutralSessionModel.ReadOnlyView
>;

namespace SparseTypes.Tests;

public sealed class DeferredHasChangesTests
{
    [Test]
    public void MultipleEditsDeferBaselineDiffUntilTheFirstReadWithoutAnotherSnapshot()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        session.Observable.Name = "first";
        session.Observable.Name = "second";

        probe.Diffs.ShouldBe(2);
        probe.Snapshots.ShouldBe(2);
        session.HasChanges.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        probe.Diffs.ShouldBe(3);
        probe.Snapshots.ShouldBe(2);

        session.Observable.Name = string.Empty;
        session.HasChanges.ShouldBeFalse();
        probe.Diffs.ShouldBe(5);
    }

    [Test]
    public void NotificationHandlersReadTheLatestStateAndReuseItsSnapshot()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        var states = new List<bool>();
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        session.TransitionObserved += changes =>
        {
            transitions.Add(changes);
            states.Add(session.HasChanges);
        };
        session.PropertyChanged += (_, _) => states.Add(session.HasChanges);

        session.Observable.Name = "edit";
        session.Observable.Name = string.Empty;

        states.ShouldBe([true, true, false, false]);
        transitions.Count.ShouldBe(2);
        transitions[1].Name.Before.Value.ShouldBe("edit");
        transitions[1].Name.After.Value.ShouldBe(string.Empty);
        probe.Diffs.ShouldBe(4);
        probe.Snapshots.ShouldBe(2);
    }

    [Test]
    public void ExplicitChangeSetCaptureCompletesThePendingCache()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        session.Observable.Name = "edit";
        session.CreateChangeSet().Name.After.Value.ShouldBe("edit");

        var snapshots = probe.Snapshots;
        var diffs = probe.Diffs;
        session.HasChanges.ShouldBeTrue();
        probe.Snapshots.ShouldBe(snapshots);
        probe.Diffs.ShouldBe(diffs);
    }

    [Test]
    public void RawModelExposureDiscardsThePendingSnapshotAndDisablesLaterCaching()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        session.Observable.Name = "edit";
        var raw = session.Model;
        raw.Name = string.Empty;
        session.HasChanges.ShouldBeFalse();
        probe.Snapshots.ShouldBe(2);

        session.Observable.Name = "another edit";
        raw.Name = string.Empty;
        session.HasChanges.ShouldBeFalse();
        probe.Snapshots.ShouldBe(4);
    }

    [Test]
    public void ReadsBeforeAnyObservableEditStillCaptureRetainedRawModelChanges()
    {
        var probe = new Probe();
        var model = new NeutralSessionModel();
        var session = probe.Create(model);
        session.HasChanges.ShouldBeFalse();
        model.Name = "raw edit";
        session.HasChanges.ShouldBeTrue();
        probe.Snapshots.ShouldBe(2);
    }

    [Test]
    public void ReadsInsideABatchDoNotReuseAPreviousPendingSnapshot()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        session.Observable.Name = "edit";
        session.BatchEdit(() =>
        {
            session.Observable.Name = string.Empty;
            session.HasChanges.ShouldBeFalse();
            session.Observable.Name = "batch edit";
        });

        probe.Snapshots.ShouldBe(3);
        probe.Diffs.ShouldBe(3);
        session.HasChanges.ShouldBeTrue();
        probe.Snapshots.ShouldBe(3);
        probe.Diffs.ShouldBe(4);
    }

    [Test]
    public void BaselineDelegateFailuresAreReportedByTheDeferredRead()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        probe.RejectBaseline = true;
        session.Observable.Name = "edit";

        Should
            .Throw<InvalidOperationException>(() => _ = session.HasChanges)
            .Message.ShouldBe("baseline diff failed");
        probe.RejectBaseline = false;
        session.HasChanges.ShouldBeTrue();
        probe.Snapshots.ShouldBe(1);
    }

    [Test]
    public void AcceptingChangesDiscardsThePendingBaselineComparison()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        session.Observable.Name = "edit";
        session.AcceptChanges();
        session.HasChanges.ShouldBeFalse();

        session.Observable.Name = "next";
        var changes = session.CreateChangeSet();
        session.Observable.Name = "last";
        session.AcceptChanges(changes);
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Name.Before.Value.ShouldBe("next");
    }

    [Test]
    public void DeferredReadsDoNotHideIsEmptyDelegateFailures()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        session.Observable.Name = "edit";
        probe.RejectIsEmpty = true;

        Should
            .Throw<InvalidOperationException>(() => _ = session.HasChanges)
            .Message.ShouldBe("Duplicate key in keyed collection.");
        probe.RejectIsEmpty = false;
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void TransitionDelegateFailuresStillOccurDuringTheEditWithoutObservers()
    {
        var probe = new Probe();
        var session = probe.Create(new NeutralSessionModel());
        probe.RejectTransition = true;

        Should
            .Throw<InvalidOperationException>(() => session.Observable.Name = "edit")
            .Message.ShouldBe("transition diff failed");
    }

    [Test]
    public void InvalidKeyedEditsDiscardThePendingSnapshotAndRecoverFromTheLastValidState()
    {
        var session = new DuplicateKeySessionModel
        {
            Items = [new DuplicateKeySessionItem { Id = "first" }],
        }.CreateEditSession();
        session.Observable.Items[0].Id = "second";
        session.Observable.Items[0].Id = "first";
        session.Observable.Items.Add(
            new DuplicateItemObservable(new DuplicateKeySessionItem { Id = "first" })
        );

        session.HasChanges.ShouldBeTrue();
        session.Observable.Items.RemoveAt(1);
        session.HasChanges.ShouldBeFalse();
    }

    private sealed class Probe
    {
        public int Snapshots { get; private set; }
        public int Diffs { get; private set; }
        public bool RejectBaseline { get; set; }
        public bool RejectIsEmpty { get; set; }
        public bool RejectTransition { get; set; }
        private NeutralSessionModel.Fragment? _baseline;

        public Session Create(NeutralSessionModel model)
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
                    Snapshots++;
                    var fragment = NeutralSessionModel.Fragment.From(current);
                    _baseline ??= fragment;
                    return fragment;
                },
                Between = (before, after) =>
                {
                    Diffs++;
                    if (RejectBaseline && ReferenceEquals(before.Value, _baseline))
                        throw new InvalidOperationException("baseline diff failed");
                    if (RejectTransition && !ReferenceEquals(before.Value, _baseline))
                        throw new InvalidOperationException("transition diff failed");
                    return NeutralSessionModel.ChangeSet.Between(before, after);
                },
                ToPatch = static changes => changes.ToPatch(),
                IsEmpty = changes =>
                    RejectIsEmpty
                        ? throw new InvalidOperationException("Duplicate key in keyed collection.")
                        : changes.IsEmpty,
                AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline),
                ToObservable = static (current, changed, access) =>
                    new NeutralObservable(current, changed, access),
                ToCurrent = static current => new NeutralView(current),
                RefreshObservable = static observable => observable.__SparseRefresh(),
            };
            var session = Session.Create(model, configuration);
            Snapshots = 0;
            Diffs = 0;
            return session;
        }
    }
}
