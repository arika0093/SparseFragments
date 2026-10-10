namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class ReloadImmutableDocument
{
    public int Version { get; init; }

    public string Title { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class ReloadCtorDocument
{
    public ReloadCtorDocument(string id) => Id = id;

    public string Id { get; }

    public string Title { get; set; } = string.Empty;
}

/// <summary>Reload must not silently drop server init/getter-only changes (#128).</summary>
public sealed class ReloadImmutableTests
{
    // Raw live-model access now hides behind ISparseEditSession<TModel>.
    private static T Raw<T>(SparseFragments.ISparseEditSession<T> session)
        where T : class => session.Model;

    [Test]
    public void ReloadRejectsServerInitChangeWithoutTouchingModelOrBaseline()
    {
        var original = new ReloadImmutableDocument { Version = 1, Title = "base" };
        var session = original.CreateEditSession();

        var result = session.Reload(new ReloadImmutableDocument { Version = 2, Title = "base" });

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.ShouldContain(static conflict =>
            conflict.PathText == nameof(ReloadImmutableDocument.Version)
        );
        ReferenceEquals(Raw(session), original).ShouldBeTrue();
        Raw(session).Version.ShouldBe(1);
        Raw(session).Title.ShouldBe("base");

        // The baseline is untouched, so nothing is pending.
        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ReloadKeepsLocalEditsWhenServerInitChangeConflicts()
    {
        var original = new ReloadImmutableDocument { Version = 1, Title = "base" };
        var session = original.CreateEditSession();
        session.Observable.Title = "local";

        var result = session.Reload(new ReloadImmutableDocument { Version = 2, Title = "base" });

        result.HasConflicts.ShouldBeTrue();
        Raw(session).Version.ShouldBe(1);
        Raw(session).Title.ShouldBe("local");

        // Failure is atomic: the pending local edit is still intact.
        var pending = session.CreateChangeSet();
        pending.Title.Before.Value.ShouldBe("base");
        pending.Title.After.Value.ShouldBe("local");
        session.HasChanges.ShouldBeTrue();

        // A server state that only touches writable members still reloads afterwards.
        var retry = session.Reload(new ReloadImmutableDocument { Version = 1, Title = "base" });
        retry.HasConflicts.ShouldBeFalse();
        Raw(session).Version.ShouldBe(1);
        Raw(session).Title.ShouldBe("local");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ReloadRejectsServerGetterOnlyChangeWithoutTouchingModel()
    {
        var original = new ReloadCtorDocument("one") { Title = "base" };
        var session = original.CreateEditSession();

        var result = session.Reload(new ReloadCtorDocument("two") { Title = "base" });

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.ShouldContain(static conflict =>
            conflict.PathText == nameof(ReloadCtorDocument.Id)
        );
        ReferenceEquals(Raw(session), original).ShouldBeTrue();
        Raw(session).Id.ShouldBe("one");
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReloadAppliesWritableServerChangesToMixedModels()
    {
        var original = new ReloadImmutableDocument { Version = 1, Title = "base" };
        var session = original.CreateEditSession();

        var result = session.Reload(new ReloadImmutableDocument { Version = 1, Title = "server" });

        result.HasConflicts.ShouldBeFalse();
        Raw(session).Version.ShouldBe(1);
        Raw(session).Title.ShouldBe("server");
        session.HasChanges.ShouldBeFalse();
    }
}
