using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>In-place writes prevalidate atomically but do not roll back (#169).</summary>
public sealed class InPlaceAtomicityTests
{
    private static EditSessionCore<
        NeutralSessionModel,
        NeutralSessionModel.Fragment,
        NeutralSessionModel.Patch,
        NeutralSessionModel.ChangeSet,
        NeutralSessionModel.Observable,
        NeutralSessionModel.ReadOnlyView
    > CreateSession(
        NeutralSessionModel model,
        NeutralSessionModel updated,
        Action<NeutralSessionModel, NeutralSessionModel> writeModel,
        List<NeutralSessionModel.ChangeSet> transitions,
        List<string?> notifications
    )
    {
        var configuration = new EditSessionCoreConfiguration<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralSessionModel.Observable,
            NeutralSessionModel.ReadOnlyView
        >
        {
            FromModel = NeutralSessionModel.Fragment.From,
            Between = NeutralSessionModel.ChangeSet.Between,
            ToPatch = static changes => changes.ToPatch(),
            IsEmpty = static changes => changes.IsEmpty,
            AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline),
            ToObservable = static (current, changed, access) =>
                new NeutralSessionModel.Observable(current, changed, access),
            ToCurrent = static current => new NeutralSessionModel.ReadOnlyView(current),
            TryApplyTo = (_, _) =>
                ((NeutralSessionModel?)updated, (IReadOnlyList<SparseConflict>?)null),
            WriteModel = (current, next) => writeModel(current, next),
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            RefreshObservable = static observable => observable.__SparseRefresh(),
        };
        var session = EditSessionCore<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralSessionModel.Observable,
            NeutralSessionModel.ReadOnlyView
        >.Create(model, configuration);
        session.TransitionObserved += transitions.Add;
        session.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        return session;
    }

    [Test]
    public void ThrowingSetterKeepsPartialModelAndResynchronizesTheSession()
    {
        var model = new NeutralSessionModel
        {
            Name = "before",
            Version = 1,
            Tags = ["kept"],
        };
        var updated = new NeutralSessionModel
        {
            Name = "after",
            Version = 2,
            Tags = ["kept"],
        };
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        var notifications = new List<string?>();
        var session = CreateSession(
            model,
            updated,
            static (current, next) =>
            {
                current.Name = next.Name;
                throw new InvalidOperationException("Version setter failed.");
            },
            transitions,
            notifications
        );
        var changes = NeutralSessionModel.ChangeSet.Between(
            new NeutralSessionModel { Name = "before", Version = 1 },
            updated
        );

        Should
            .Throw<InvalidOperationException>(() => session.TryApplyInPlace(changes, out _))
            .Message.ShouldBe("Version setter failed.");

        // The failure is not atomic over external setters, but the session heals.
        model.Name.ShouldBe("after");
        model.Version.ShouldBe(1);
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Name.After.Value.ShouldBe("after");
        transitions.Count.ShouldBe(1);
        notifications.ShouldContain(nameof(session.HasChanges));
    }

    [Test]
    public void ThrowingCollectionWriteKeepsPartialStateAndResynchronizesTheSession()
    {
        var model = new NeutralSessionModel { Tags = ["one", "two"] };
        var updated = new NeutralSessionModel { Tags = ["three"] };
        var session = CreateSession(
            model,
            updated,
            static (current, _) =>
            {
                current.Tags.Clear();
                throw new InvalidOperationException("AddRange failed.");
            },
            [],
            []
        );
        var changes = NeutralSessionModel.ChangeSet.Between(new NeutralSessionModel(), updated);

        Should.Throw<InvalidOperationException>(() => session.TryApplyInPlace(changes, out _));

        model.Tags.ShouldBeEmpty();
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Tags.After.Value.ShouldBeEmpty();
    }

    [Test]
    public void BatchEditDoesNotRollBackTheModelWhenTheDelegateThrows()
    {
        var model = new NeutralSessionModel { Name = "base", Version = 1 };
        var session = model.CreateEditSession();

        Should.Throw<InvalidOperationException>(() =>
            session.BatchEdit(() =>
            {
                session.Observable.Name = "partial";
                session.Observable.Version = 2;
                throw new InvalidOperationException("edit failed");
            })
        );

        model.Name.ShouldBe("partial");
        model.Version.ShouldBe(2);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ScalarConflictLeavesTheLiveModelUntouched()
    {
        var baseline = new NeutralSessionModel { Name = "base" };
        var live = new NeutralSessionModel { Name = "concurrent" };
        var session = baseline.CreateEditSession(live);
        var changes = NeutralSessionModel.ChangeSet.Between(
            baseline,
            new NeutralSessionModel { Name = "edited" }
        );

        session.TryApplyInPlace(changes, out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        conflicts.ShouldContain(static conflict =>
            conflict.PathText == nameof(NeutralSessionModel.Name)
        );
        live.Name.ShouldBe("concurrent");
        session.HasChanges.ShouldBeTrue();
    }
}
