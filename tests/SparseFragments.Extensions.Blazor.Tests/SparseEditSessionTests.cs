using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Extensions.Blazor.Tests;

public sealed class SparseEditSessionTests
{
    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine { Sku = "a", Quantity = 1, Price = 10m },
                new OrderLine { Sku = "b", Quantity = 2, Price = 20m },
            },
            Tags = new() { "fragile" },
        };

    [Test]
    public void ScalarFieldEditProducesPatch()
    {
        var session = Order().CreateEditSession();
        session.HasChanges.ShouldBeFalse();

        session.Model.Number = "ORD-2";
        session.HasChanges.ShouldBeTrue();

        var patch = session.CreatePatch();
        patch.IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void NestedStructuralEditDetected()
    {
        var session = Order().CreateEditSession();

        session.Model.Customer.Email = "new@example.com";
        session.HasChanges.ShouldBeTrue();
        session.CreatePatch().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void EditThenRestoreHasNoSemanticChanges()
    {
        var session = Order().CreateEditSession();

        session.Model.Number = "changed";
        session.Model.Number = "ORD-1";

        session.HasChanges.ShouldBeFalse();
        session.CreatePatch().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ValidationBehaviorThroughEditContext()
    {
        var session = Order().CreateEditSession();
        var store = session.CreateValidationStore();
        session.EditContext.OnValidationRequested += (sender, _) =>
        {
            store.Clear();
            if (string.IsNullOrEmpty(session.Model.Number))
            {
                store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        };

        session.EditContext.Validate().ShouldBeTrue();

        session.Model.Number = "";
        session.EditContext.NotifyFieldChanged(
            new FieldIdentifier(session.Model, nameof(OrderDto.Number))
        );
        session.EditContext.Validate().ShouldBeFalse();
        session.EditContext.GetValidationMessages().ShouldContain("Number is required.");
    }

    [Test]
    public void KeyedCollectionAddRemoveEditSemantics()
    {
        var baseline = Order();
        var session = baseline.CreateEditSession();

        session.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });
        session.Model.Lines.RemoveAll(line => line.Sku == "a");
        session.Model.Lines.Single(line => line.Sku == "b").Quantity = 9;

        var patch = session.CreatePatch();
        var applied = OrderDto.Fragment.From(
            new OrderDto
            {
                Number = "ORD-1",
                Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                Lines = new()
                {
                    new OrderLine { Sku = "a", Quantity = 1, Price = 10m },
                    new OrderLine { Sku = "b", Quantity = 2, Price = 20m },
                },
                Tags = new() { "fragile" },
            }
        ).Apply(patch);

        var lines = applied.Lines.Value!;
        lines.Select(line => line.Sku).ShouldBe(["b", "c"]);
        lines.Single(line => line.Sku == "b").Quantity.ShouldBe(9);
    }

    [Test]
    public void KeyedCollectionReorder()
    {
        var session = Order().CreateEditSession();

        session.Model.Lines = session.Model.Lines.AsEnumerable().Reverse().ToList();

        session.HasChanges.ShouldBeTrue();
        var patch = session.CreatePatch();
        var applied = OrderDto.Fragment.From(Order()).Apply(patch);
        applied.Lines.Value!.Select(line => line.Sku).ShouldBe(["b", "a"]);
    }

    [Test]
    public void ScalarCollectionReplacement()
    {
        var session = Order().CreateEditSession();

        session.Model.Tags.Add("heavy");
        session.HasChanges.ShouldBeTrue();

        var patch = session.CreatePatch();
        var applied = OrderDto.Fragment.From(Order()).Apply(patch);
        applied.Tags.Value!.ShouldBe(["fragile", "heavy"]);

        session.Model.Tags = session.Model.Tags.AsEnumerable().Reverse().ToList();
        var reorderPatch = session.CreatePatch();
        var reordered = OrderDto.Fragment.From(Order()).Apply(reorderPatch);
        reordered.Tags.Value!.ShouldBe(["heavy", "fragile"]);
    }

    [Test]
    public void NestedKeyedCollections()
    {
        var model = new OrgDto
        {
            Teams = new()
            {
                new Team
                {
                    Name = "t1",
                    Members = new() { new TeamMember { Id = "m1", Skills = new() { "c#" } } },
                },
            },
        };
        var session = model.CreateEditSession();
        session.HasChanges.ShouldBeFalse();

        session.Model.Teams.Single(t => t.Name == "t1").Members.Add(new TeamMember { Id = "m2" });
        session.HasChanges.ShouldBeTrue();

        var patch = session.CreatePatch();
        patch.IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void CollectionMutationWithoutFieldNotificationDetected()
    {
        var session = Order().CreateEditSession();

        // No EditContext.NotifyFieldChanged here: direct list mutation.
        session.Model.Lines.RemoveAt(0);

        session.EditContext.IsModified().ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void AcceptChangesResetsBaselineAndBlazorState()
    {
        var session = Order().CreateEditSession();

        session.Model.Number = "ORD-2";
        session.EditContext.NotifyFieldChanged(
            new FieldIdentifier(session.Model, nameof(OrderDto.Number))
        );
        session.EditContext.IsModified().ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();

        session.AcceptChanges();

        session.HasChanges.ShouldBeFalse();
        session.EditContext.IsModified().ShouldBeFalse();

        session.Model.Number = "ORD-3";
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void RepeatedCreateAcceptEditCycles()
    {
        var session = Order().CreateEditSession();

        for (var i = 0; i < 3; i++)
        {
            session.Model.Number = "ORD-" + i;
            session.HasChanges.ShouldBeTrue();
            session.CreatePatch().IsEmpty.ShouldBeFalse();
            session.AcceptChanges();
            session.HasChanges.ShouldBeFalse();
        }
    }

    [Test]
    public void ValidationMessageStorePrimitive()
    {
        var session = Order().CreateEditSession();
        var store = session.CreateValidationStore();

        SparseEditSession<OrderDto, OrderDto.Fragment, OrderDto.Patch>.AddValidationError(
            store,
            session.Field(nameof(OrderDto.Number)),
            "Server rejected the order number."
        );

        session.EditContext.GetValidationMessages().ShouldContain("Server rejected the order number.");
    }
}
