using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor.Tests;

public sealed class BlazorUnassignedLifecycleTests
{
    // Raw live-model access now hides behind ISparseEditSession<TModel>.
    private static T Raw<T>(SparseFragments.ISparseEditSession<T> session)
        where T : class => session.Model;

    private static BlazorUnassignedOrder Order() =>
        new()
        {
            Number = "ORD-1",
            Items = new()
            {
                new BlazorUnassignedItem { Id = 7, Name = "existing" },
            },
        };

    private static Optional<BlazorUnassignedOrder.Fragment?> State(BlazorUnassignedOrder model) =>
        Optional<BlazorUnassignedOrder.Fragment?>.Present(
            BlazorUnassignedOrder.Fragment.From(model)
        );

    [Test]
    public void UnassignedAddsAreIndependentThroughPayload()
    {
        var before = State(Order());
        var after = State(
            new BlazorUnassignedOrder
            {
                Number = "ORD-1",
                Items = new()
                {
                    new BlazorUnassignedItem { Id = 0, Name = "first" },
                    new BlazorUnassignedItem { Id = 7, Name = "existing" },
                    new BlazorUnassignedItem { Id = 0, Name = "second" },
                },
            }
        );

        var changes = BlazorUnassignedOrder.ChangeSet.Between(before, after);
        changes.Items.Added.Select(item => item.Name).ShouldBe(["first", "second"]);
        changes.Items.GetChange(0).IsEmpty.ShouldBeTrue();

        var json = System.Text.Json.JsonSerializer.Serialize(changes.ToPayload());
        var restored = System
            .Text.Json.JsonSerializer.Deserialize<BlazorUnassignedOrder.ChangePayload>(json)!
            .ToChangeSet();
        restored
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);
    }

    [Test]
    public void AcceptChangesWithUnassignedAddRejectsWithoutMutatingBaseline()
    {
        var session = Order().CreateEditSession();
        Raw(session).Items.Add(new BlazorUnassignedItem { Id = 0, Name = "first" });
        var outgoing = session.CreateChangeSet();

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(outgoing));
        session.HasChanges.ShouldBeTrue();

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges());
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ServerRehydrateResetsSessionAndRecreatesEditContext()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        Raw(session).Items.Add(new BlazorUnassignedItem { Id = 0, Name = "first" });
        Raw(session).Items.Add(new BlazorUnassignedItem { Id = 0, Name = "second" });
        var outgoing = session.CreateChangeSet();
        outgoing.IsEmpty.ShouldBeFalse();

        // Server persists with distinct IDs and normalized fields.
        var persisted = new BlazorUnassignedOrder
        {
            Number = "ORD-1",
            Items = new()
            {
                new BlazorUnassignedItem { Id = 7, Name = "existing" },
                new BlazorUnassignedItem { Id = 11, Name = "first" },
                new BlazorUnassignedItem { Id = 12, Name = "second" },
            },
        };

        // Fresh session on the authoritative model; recreate the EditContext.
        session = persisted.CreateEditSession();
        var freshContext = session.CreateEditContext();

        ReferenceEquals(freshContext.Model, persisted).ShouldBeTrue();
        ReferenceEquals(editContext.Model, Raw(session)).ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        freshContext.IsModified().ShouldBeFalse();

        // The next authoritative edit is acknowledged with context sync.
        Raw(session).Number = "ORD-2";
        var acknowledged = session.CreateChangeSet();
        session.AcceptChanges(freshContext, acknowledged);
        session.HasChanges.ShouldBeFalse();
        freshContext.IsModified().ShouldBeFalse();

        // A later edit keeps the context modified.
        Raw(session).Number = "ORD-3";
        session.HasChanges.ShouldBeTrue();
    }
}
