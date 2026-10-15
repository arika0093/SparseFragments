using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>State queries against Accept/Revert/Reload/reconcile baselines.</summary>
public sealed class SessionStateLifecycleTests
{
    [Test]
    public void AcceptAdvancesBaseline()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Label = "dirty";
        session.FieldState(Settings.SparsePath.Label).IsChanged.ShouldBeTrue();

        session.AcceptChanges();

        session.FieldState(Settings.SparsePath.Label).HasChanges.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void AcceptSubmittedChangeSetKeepsPostSubmitEdits()
    {
        var session = new Settings { Label = "a" }.CreateEditSession();
        session.Observable.Label = "b";
        var submitted = session.CreateChangeSet();
        session.Observable.Label = "c";

        session.AcceptChanges(submitted);

        // Later edits stay pending against the advanced baseline.
        var state = session.FieldState(Settings.SparsePath.Label);
        state.IsChanged.ShouldBeTrue();
        state.Before.Value.ShouldBe("b");
        state.After.Value.ShouldBe("c");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void RevertRestoresBaselineState()
    {
        var session = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "a", Name = "a" }],
        }.CreateEditSession();
        session.Observable.Items[0].Name = "dirty";
        session.Observable.Items.AddModel(new KeyedServer { Id = "b", Name = "b" });

        session.RevertChanges();

        session
            .FieldState(KeyedServerHolder.SparsePath.Items.Key("a").Name)
            .HasChanges.ShouldBeFalse();
        session
            .CollectionState(KeyedServerHolder.SparsePath.Items)
            .IsChanged.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReloadRebasesBaseline()
    {
        var session = new Settings { Label = "base", RetryCount = 1 }.CreateEditSession();
        session.Observable.Label = "local";
        var server = new Settings { Label = "base", RetryCount = 2 };

        var result = session.Reload(server);
        result.HasConflicts.ShouldBeFalse();

        // Local edits stay pending; adopted server state becomes the baseline.
        var label = session.FieldState(Settings.SparsePath.Label);
        label.IsChanged.ShouldBeTrue();
        label.Before.Value.ShouldBe("base");
        label.After.Value.ShouldBe("local");

        session.FieldState(Settings.SparsePath.RetryCount).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReloadConflictLeavesBaselineDeterministic()
    {
        var session = new Settings { Label = "base" }.CreateEditSession();
        session.Observable.Label = "local";
        var server = new Settings { Label = "server" };

        var first = session.Reload(server);
        first.HasConflicts.ShouldBeTrue();

        var before = session.FieldState(Settings.SparsePath.Label);
        before.IsChanged.ShouldBeTrue();
        before.Before.Value.ShouldBe("base");
        before.After.Value.ShouldBe("local");

        // A failed reload mutates nothing: repeating it reports the same state.
        var second = session.Reload(server);
        second.HasConflicts.ShouldBeTrue();
        var after = session.FieldState(Settings.SparsePath.Label);
        after.ShouldBe(before);
    }

    [Test]
    public void ReconcileAdoptsPersistedBaseline()
    {
        var firstTemp = Guid.NewGuid();
        var lateTemp = Guid.NewGuid();
        var model = new TempOrderHolder();
        var session = model.CreateEditSession();
        model.Lines.Add(new TempOrderLine { Name = "one", TemporaryId = firstTemp });
        var submitted = session.CreateChangeSet();

        // Post-submit local edits are preserved across reconcile.
        model.Lines.Add(new TempOrderLine { Name = "two", TemporaryId = lateTemp });

        // Fake store: assigns a permanent key and normalizes the value while
        // echoing the temporary identity on the response row.
        var persisted = new TempOrderHolder
        {
            Lines =
            [
                new TempOrderLine { Id = 1001, Name = "ONE", TemporaryId = firstTemp },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        // Untouched rows adopt persisted values; the late row stays pending.
        model.Lines.Select(item => item.Id).ShouldBe([1001, 0]);
        model.Lines.Select(item => item.Name).ShouldBe(["ONE", "two"]);

        // The persisted model is the new baseline: only post-submit edits
        // remain.
        var view = session.CollectionState(TempOrderHolder.SparsePath.Lines);
        view.IsChanged.ShouldBeTrue();
        view.GetItemState(1001).IsEmpty.ShouldBeTrue();

        var late = view.GetTemporaryState(lateTemp);
        late.IsAdded.ShouldBeTrue();
        late.After.Value.ShouldBeOfType<TempOrderLine>().Name.ShouldBe("two");

        var member = session.FieldState(TempOrderHolder.SparsePath.Lines);
        member.HasChanges.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
    }
}
