using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor.Tests;

// Cached binding metadata and value expressions: repeated reads reuse stable
// trees and metadata while nested replacement stays live.
public sealed class SparseBindingCacheTests
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
                new OrderLine
                {
                    Sku = "b",
                    Quantity = 2,
                    Price = 20m,
                },
            },
        };

    [Test]
    public void MemberExpressionTreeIsReusedWhileRootHolds()
    {
        var session = Order().CreateEditSession();
        var binding = session.Bind(OrderDto.SparsePath.Customer.Name, session.CreateEditContext());

        var first = binding.ValueExpression;
        first.ShouldNotBeNull();
        ReferenceEquals(first, binding.ValueExpression).ShouldBeTrue();
        ReferenceEquals(first, binding.ValueExpression).ShouldBeTrue();
        binding.Value.ShouldBe("Ada");
    }

    [Test]
    public void ReusedExpressionFollowsNestedReplacement()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var binding = session.Bind(OrderDto.SparsePath.Customer.Name, session.CreateEditContext());

        var before = binding.ValueExpression;
        before.ShouldNotBeNull();

        live.Customer = new OrderCustomer { Name = "Grace" };

        // Same root identity: the cached tree re-evaluates to the replacement.
        ReferenceEquals(before, binding.ValueExpression).ShouldBeTrue();
        binding.Value.ShouldBe("Grace");
        binding.Field.Model.ShouldBeSameAs(live.Customer);
    }

    [Test]
    public void WarmedRepeatedReadsStayCorrectAcrossShapes()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var scalar = session.Bind(OrderDto.SparsePath.Number, context);
        var nested = session.Bind(OrderDto.SparsePath.Customer.Name, context);
        var keyed = session.Bind(OrderDto.SparsePath.Lines.Key("b").Quantity, context);

        for (var index = 0; index < 25; index++)
        {
            scalar.Value.ShouldBe("ORD-1");
            nested.Value.ShouldBe("Ada");
            keyed.Value.ShouldBe(2);
            scalar.Field.FieldName.ShouldBe(nameof(OrderDto.Number));
            nested.Field.FieldName.ShouldBe(nameof(OrderCustomer.Name));
            keyed.Field.FieldName.ShouldBe(nameof(OrderLine.Quantity));
        }
    }

    [Test]
    public void KeyedRemovalFailsAfterWarmedHits()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var binding = session.Bind(
            OrderDto.SparsePath.Lines.Key("a").Quantity,
            session.CreateEditContext()
        );

        binding.Value.ShouldBe(1);
        binding.Value.ShouldBe(1);

        live.Lines.RemoveAll(line => line.Sku == "a");

        binding.IsStale.ShouldBeTrue();
    }

    [Test]
    public void CollectionPathsKeepNullExpressions()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var keyed = session.Bind(OrderDto.SparsePath.Lines.Key("b").Quantity, context);

        // Keyed paths never expose a fabricated expression, cached or not.
        keyed.ValueExpression.ShouldBeNull();
        keyed.ValueExpression.ShouldBeNull();
        keyed.Value.ShouldBe(2);
    }
}
