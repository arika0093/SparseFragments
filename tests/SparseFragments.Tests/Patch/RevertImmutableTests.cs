namespace SparseFragments.Tests;

/// <summary>RevertChanges on immutable models needs an explicit recovery path (#171).</summary>
public sealed class RevertImmutableTests
{
    [Test]
    public void TryRevertChangesReportsInitDriftWithoutTouchingTheModel()
    {
        var baseline = new ReloadImmutableDocument { Version = 1, Title = "base" };
        var current = new ReloadImmutableDocument { Version = 2, Title = "base" };
        var session = baseline.CreateEditSession(current);

        session.HasChanges.ShouldBeTrue();
        session.TryRevertChanges(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        conflicts.ShouldContain(static conflict =>
            conflict.PathText == nameof(ReloadImmutableDocument.Version)
            && conflict.Kind == SparseConflictKind.InPlaceWriteUnavailable
        );
        ReferenceEquals(session.Model, current).ShouldBeTrue();
        current.Version.ShouldBe(2);
        session.HasChanges.ShouldBeTrue();

        Should.Throw<InvalidOperationException>(() => session.RevertChanges());
        current.Version.ShouldBe(2);

        // The defined recovery is a fresh session, not an in-place restore.
        baseline.CreateEditSession(baseline).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void TryRevertChangesReportsGetterOnlyDriftWithoutTouchingTheModel()
    {
        var baseline = new ReloadCtorDocument("one") { Title = "base" };
        var current = new ReloadCtorDocument("two") { Title = "base" };
        var session = baseline.CreateEditSession(current);

        session.TryRevertChanges(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        conflicts.ShouldContain(static conflict =>
            conflict.PathText == nameof(ReloadCtorDocument.Id)
            && conflict.Kind == SparseConflictKind.InPlaceWriteUnavailable
        );
        ReferenceEquals(session.Model, current).ShouldBeTrue();
        current.Id.ShouldBe("two");
    }

    [Test]
    public void TryRevertChangesKeepsMutableEditsWhenImmutableDriftBlocks()
    {
        var baseline = new ReloadImmutableDocument { Version = 1, Title = "base" };
        var current = new ReloadImmutableDocument { Version = 2, Title = "edited" };
        var session = baseline.CreateEditSession(current);

        session.TryRevertChanges(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        conflicts.ShouldNotBeEmpty();
        current.Version.ShouldBe(2);
        current.Title.ShouldBe("edited");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void TryRevertChangesRestoresMutableModelsInPlace()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();
        session.Observable.Name = "after";
        session.Observable.Version = 2;

        session.TryRevertChanges(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        ReferenceEquals(session.Model, model).ShouldBeTrue();
        model.Name.ShouldBe("before");
        model.Version.ShouldBe(1);
        session.HasChanges.ShouldBeFalse();
    }
}
