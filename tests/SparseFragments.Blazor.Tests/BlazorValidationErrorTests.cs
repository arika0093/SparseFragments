using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor.Tests;

public sealed class BlazorValidationErrorTests
{
    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
            },
            Contacts = new() { ["billing"] = new OrderCustomer { Name = "Ada" } },
        };

    [Test]
    public void NestedSessionFieldAccepted()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);

        session.AddValidationError(store, session.Field("Customer.Name"), "Required.");

        editContext
            .GetValidationMessages(session.Field("Customer.Name"))
            .ShouldContain("Required.");
    }

    [Test]
    public void ListElementAndDictionaryValueFieldsAccepted()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);

        session.AddValidationError(store, session.Field("Lines[0].Quantity"), "Must be positive.");
        session.AddValidationError(store, session.Field("Contacts[\"billing\"].Name"), "Required.");

        editContext
            .GetValidationMessages(session.Field("Lines[0].Quantity"))
            .ShouldContain("Must be positive.");
        editContext
            .GetValidationMessages(session.Field("Contacts[\"billing\"].Name"))
            .ShouldContain("Required.");
    }

    [Test]
    public void PathBasedOverloadResolvesSessionFields()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);

        session.AddValidationError(store, "Customer.Name", "Required.");

        editContext
            .GetValidationMessages(session.Field("Customer.Name"))
            .ShouldContain("Required.");
        Should.Throw<ArgumentException>(() =>
            session.AddValidationError(store, "Customer.Unknown", "No such member.")
        );
    }

    [Test]
    public void UnrelatedSiblingGraphRejected()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);
        var sibling = Order().CreateEditSession();

        var thrown = Should.Throw<ArgumentException>(() =>
            session.AddValidationError(store, sibling.Field("Customer.Name"), "Wrong graph.")
        );
        thrown.ParamName.ShouldBe("field");
        editContext.GetValidationMessages().ShouldBeEmpty();
    }

    [Test]
    public void DetachedElementRejected()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);
        var detached = new FieldIdentifier(
            new OrderCustomer { Name = "Zed" },
            nameof(OrderCustomer.Name)
        );

        Should.Throw<ArgumentException>(() =>
            session.AddValidationError(store, detached, "Detached.")
        );
        editContext.GetValidationMessages().ShouldBeEmpty();
    }
}
