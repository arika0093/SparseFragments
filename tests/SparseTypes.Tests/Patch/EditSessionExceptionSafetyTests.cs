using SparseTypes.Generated;

namespace SparseTypes.Tests;

/// <summary>Exception-safety contract for state-mutating edit-session operations.</summary>
/// <remarks>Faults are injected at the same seams the generator wires (<c>WriteModel</c> standing in for a throwing custom setter.</remarks>
public sealed class EditSessionExceptionSafetyTests
{
    private const string SetterFailure = "Version setter failed.";
    private const string RetargetFailure = "Retarget callback failed.";
    private const string RefreshFailure = "Refresh callback failed.";
    private const string RollbackFailure = "Rollback writer failed.";
    private const string CorrelationFailure = "Correlation failed.";
    private const string RetargetError = "The live state is incompatible.";

    private static EditSessionCore<
        NeutralSessionModel,
        NeutralSessionModel.Fragment,
        NeutralSessionModel.Patch,
        NeutralSessionModel.ChangeSet,
        NeutralObservable,
        NeutralView
    > CreateSession(
        NeutralSessionModel model,
        Action<NeutralSessionModel, NeutralSessionModel> writeModel,
        Func<bool> refreshShouldThrow,
        List<NeutralSessionModel.ChangeSet> transitions,
        List<string?> notifications
    )
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
            FromModel = NeutralSessionModel.Fragment.From,
            Between = NeutralSessionModel.ChangeSet.Between,
            ToPatch = static changes => changes.ToPatch(),
            IsEmpty = static changes => changes.IsEmpty,
            AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline),
            ToObservable = static (current, changed, access) =>
                new NeutralObservable(current, changed, access),
            ToCurrent = static current => new NeutralView(current),
            TryApplyTo = static (changes, current) =>
                changes.TryApplyTo(current, out var updated, out var conflicts)
                    ? (updated, null)
                    : (null, conflicts),
            WriteModel = writeModel,
            Rebase = static (changes, server) => changes.RebaseOnto(server),
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            RefreshObservable = observable =>
            {
                if (refreshShouldThrow())
                {
                    throw new InvalidOperationException(RefreshFailure);
                }

                observable.__SparseRefresh();
            },
            BaselineToModel = static fragment => fragment.ToModel(),
        };
        var session = EditSessionCore<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralObservable,
            NeutralView
        >.Create(model, configuration);
        session.TransitionObserved += transitions.Add;
        session.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        return session;
    }

    [Test]
    public void ThrowingWriteModelResynchronizesTryApplyInPlace()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var updated = new NeutralSessionModel { Name = "after", Version = 2 };
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        var notifications = new List<string?>();
        var writeShouldThrow = true;
        var session = CreateSession(
            model,
            (current, next) =>
            {
                // A custom setter mutates one member, then throws.
                current.Name = next.Name;
                if (writeShouldThrow)
                {
                    throw new InvalidOperationException(SetterFailure);
                }

                current.Version = next.Version;
            },
            static () => false,
            transitions,
            notifications
        );
        var changes = NeutralSessionModel.ChangeSet.Between(
            new NeutralSessionModel { Name = "before", Version = 1 },
            updated
        );

        Should
            .Throw<InvalidOperationException>(() => session.TryApplyInPlace(changes, out _))
            .Message.ShouldBe(SetterFailure);

        // The failure is not atomic over external setters, but the session
        // heals.
        model.Name.ShouldBe("after");
        model.Version.ShouldBe(1);
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Name.After.Value.ShouldBe("after");
        session.Observable.Name.ShouldBe("after");
        transitions.Count.ShouldBe(1);
        notifications.ShouldContain(nameof(session.HasChanges));

        // A healthy retry still commits against the untouched baseline.
        writeShouldThrow = false;
        session.TryApplyInPlace(changes, out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        model.Version.ShouldBe(2);
        session.CreateChangeSet().Version.After.Value.ShouldBe(2);
    }

    [Test]
    public void ThrowingWriteModelResynchronizesReloadWithoutAdoptingBaseline()
    {
        var model = new NeutralSessionModel { Name = "base", Version = 1 };
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        var notifications = new List<string?>();
        var writeShouldThrow = true;
        var refreshShouldThrow = false;
        var session = CreateSession(
            model,
            (current, next) =>
            {
                // A custom setter mutates one member, then throws.
                current.Name = next.Name;
                if (writeShouldThrow)
                {
                    throw new InvalidOperationException(SetterFailure);
                }

                current.Version = next.Version;
            },
            () => refreshShouldThrow,
            transitions,
            notifications
        );
        session.Observable.Version = 10;
        var server = new NeutralSessionModel { Name = "server", Version = 1 };
        var transitionsBeforeReload = transitions.Count;

        Should
            .Throw<InvalidOperationException>(() => session.Reload(server))
            .Message.ShouldBe(SetterFailure);

        // The partial write stays visible, while the server baseline is not
        // adopted.
        model.Name.ShouldBe("server");
        model.Version.ShouldBe(10);
        session.Observable.Name.ShouldBe("server");
        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Name.Before.Value.ShouldBe("base");
        pending.Name.After.Value.ShouldBe("server");
        pending.Version.Before.Value.ShouldBe(1);
        pending.Version.After.Value.ShouldBe(10);
        transitions.Count.ShouldBe(transitionsBeforeReload + 1);
        notifications.ShouldContain(nameof(session.HasChanges));

        // A failing resynchronization must not mask the original exception,
        // nor publish a false success transition.
        refreshShouldThrow = true;
        Should
            .Throw<InvalidOperationException>(() => session.Reload(server))
            .Message.ShouldBe(SetterFailure);
        transitions.Count.ShouldBe(transitionsBeforeReload + 1);
        refreshShouldThrow = false;

        // A healthy retry still rebases onto the server and adopts its
        // baseline.
        writeShouldThrow = false;
        var retry = session.Reload(server);
        retry.HasConflicts.ShouldBeFalse();
        model.Name.ShouldBe("server");
        model.Version.ShouldBe(10);
        var settled = session.CreateChangeSet();
        settled.Name.IsChanged.ShouldBeFalse();
        settled.Version.Before.Value.ShouldBe(1);
        settled.Version.After.Value.ShouldBe(10);
    }

    [Test]
    public void ThrowingRetargetRestoresAndResynchronizesTryReconcile()
    {
        var model = new NeutralSessionModel { Name = "base", Version = 1 };
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        var notifications = new List<string?>();
        var refreshShouldThrow = false;
        var session = CreateSession(
            model,
            static (current, next) =>
            {
                current.Name = next.Name;
                current.Version = next.Version;
            },
            () => refreshShouldThrow,
            transitions,
            notifications
        );
        session.Observable.Name = "local";
        var submitted = session.CreateChangeSet();
        var persisted = new NeutralSessionModel { Name = "base", Version = 1 };
        var retargetShouldThrow = true;
        Func<
            NeutralSessionModel,
            NeutralSessionModel,
            NeutralSessionModel,
            object?,
            NeutralSessionModel.ChangeSet,
            string?
        > retarget = (live, _, _, _, _) =>
        {
            if (retargetShouldThrow)
            {
                // A custom callback mutates one member, then throws.
                live.Name = "partial";
                throw new InvalidOperationException(RetargetFailure);
            }

            live.Name = "reconciled";
            return null;
        };
        static (object? Map, string? Error) BuildMap(
            NeutralSessionModel _,
            NeutralSessionModel __
        ) => (new object(), (string?)null);

        Should
            .Throw<InvalidOperationException>(() =>
                session.TryReconcile(submitted, persisted, BuildMap, retarget, out _)
            )
            .Message.ShouldBe(RetargetFailure);

        // The healthy rollback writer restored the pre-call snapshot.
        model.Name.ShouldBe("local");
        session.Observable.Name.ShouldBe("local");
        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Name.Before.Value.ShouldBe("base");
        pending.Name.After.Value.ShouldBe("local");
        transitions.Count.ShouldBe(1);
        notifications.ShouldContain(nameof(session.HasChanges));

        // A failing resynchronization must not mask the original exception.
        refreshShouldThrow = true;
        Should
            .Throw<InvalidOperationException>(() =>
                session.TryReconcile(submitted, persisted, BuildMap, retarget, out _)
            )
            .Message.ShouldBe(RetargetFailure);
        transitions.Count.ShouldBe(1);
        refreshShouldThrow = false;

        // A healthy retry still reconciles against the untouched baseline.
        retargetShouldThrow = false;
        session
            .TryReconcile(submitted, persisted, BuildMap, retarget, out var error)
            .ShouldBeTrue();
        error.ShouldBeNull();
        model.Name.ShouldBe("reconciled");
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Name.After.Value.ShouldBe("reconciled");
    }

    [Test]
    public void RetargetErrorLeavesLiveAndBaselineUntouched()
    {
        var model = new NeutralSessionModel
        {
            Name = "base",
            Version = 1,
            Tags = ["kept"],
        };
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        var notifications = new List<string?>();
        var session = CreateSession(
            model,
            static (current, next) =>
            {
                current.Name = next.Name;
                current.Version = next.Version;
            },
            static () => false,
            transitions,
            notifications
        );
        session.Observable.Name = "local";
        var tags = model.Tags;
        var submitted = session.CreateChangeSet();
        var persisted = new NeutralSessionModel
        {
            Name = "base",
            Version = 1,
            Tags = ["kept"],
        };

        session
            .TryReconcile<object>(
                submitted,
                persisted,
                static (_, _) => (new object(), (string?)null),
                static (_, _, _, _, _) => RetargetError,
                out var error
            )
            .ShouldBeFalse();
        error.ShouldBe(RetargetError);

        // Nothing diverged, so the backup was never written back over the live
        // references.
        model.Name.ShouldBe("local");
        ReferenceEquals(model.Tags, tags).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Name.Before.Value.ShouldBe("base");
        pending.Name.After.Value.ShouldBe("local");
        session.Observable.Name.ShouldBe("local");

        // A compatible retry still succeeds afterwards.
        session
            .TryReconcile<object>(
                submitted,
                persisted,
                static (_, _) => (new object(), (string?)null),
                static (live, _, _, _, _) =>
                {
                    live.Name = "reconciled";
                    return (string?)null;
                },
                out error
            )
            .ShouldBeTrue();
        error.ShouldBeNull();
    }

    [Test]
    public void ThrowingRetargetWithFailingRollbackPreservesOriginalAndObservesPartialWrite()
    {
        var model = new NeutralSessionModel { Name = "base", Version = 1 };
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        var notifications = new List<string?>();
        var session = CreateSession(
            model,
            static (current, next) =>
            {
                current.Name = next.Name;
                throw new InvalidOperationException(RollbackFailure);
            },
            static () => false,
            transitions,
            notifications
        );
        session.Observable.Name = "local";
        var submitted = session.CreateChangeSet();
        var persisted = new NeutralSessionModel { Name = "base", Version = 1 };
        var transitionsBeforeReconcile = transitions.Count;

        // True rollback is impossible with arbitrary setters.
        Should
            .Throw<InvalidOperationException>(() =>
                session.TryReconcile<object>(
                    submitted,
                    persisted,
                    static (_, _) => (new object(), (string?)null),
                    static (live, _, _, _, _) =>
                    {
                        live.Name = "partial";
                        live.Version = 99;
                        throw new InvalidOperationException(RetargetFailure);
                    },
                    out _
                )
            )
            .Message.ShouldBe(RetargetFailure);

        model.Name.ShouldBe("local");
        model.Version.ShouldBe(99);
        session.Observable.Name.ShouldBe("local");
        session.Observable.Version.ShouldBe(99);
        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Name.Before.Value.ShouldBe("base");
        pending.Name.After.Value.ShouldBe("local");
        pending.Version.Before.Value.ShouldBe(1);
        pending.Version.After.Value.ShouldBe(99);
        transitions.Count.ShouldBe(transitionsBeforeReconcile + 1);
        notifications.ShouldContain(nameof(session.HasChanges));
    }

    [Test]
    public void ThrowingBuildMapLeavesLiveAndBaselineUntouched()
    {
        var model = new NeutralSessionModel { Name = "base", Version = 1 };
        var transitions = new List<NeutralSessionModel.ChangeSet>();
        var notifications = new List<string?>();
        var session = CreateSession(
            model,
            static (current, next) =>
            {
                current.Name = next.Name;
                current.Version = next.Version;
            },
            static () => false,
            transitions,
            notifications
        );
        session.Observable.Name = "local";
        var submitted = session.CreateChangeSet();
        var persisted = new NeutralSessionModel { Name = "base", Version = 1 };

        Should
            .Throw<InvalidOperationException>(() =>
                session.TryReconcile<object>(
                    submitted,
                    persisted,
                    static (_, _) => throw new InvalidOperationException(CorrelationFailure),
                    static (_, _, _, _, _) => (string?)null,
                    out _
                )
            )
            .Message.ShouldBe(CorrelationFailure);

        model.Name.ShouldBe("local");
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Name.After.Value.ShouldBe("local");
        session.Observable.Name.ShouldBe("local");
    }
}
