using Microsoft.AspNetCore.Components;

namespace SparseTypes.Blazor.Tests;

// One shared state-query snapshot per edit-scope refresh generation: fields
// reuse the capture instead of deriving a root diff each.
public sealed class SparseSnapshotSharingTests
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
        };

    [Test]
    public async Task FieldsShareOneSnapshotPerGeneration()
    {
        var session = Order().CreateEditSession();
        SparseFieldContext<OrderDto, string>? numberField = null;
        SparseFieldContext<OrderDto, string>? nameField = null;
        var generations = new List<SparseChangeSetQueryView?>();

        var fragment = Headless.Edit(
            session,
            static _ => { },
            edit =>
            {
                // Every capture inside one render observes the same generation.
                edit.TryGetStateSnapshot(out var first, out var firstError);
                edit.TryGetStateSnapshot(out var second, out var secondError);
                firstError.ShouldBeNull();
                secondError.ShouldBeNull();
                first.ShouldNotBeNull();
                ReferenceEquals(first, second).ShouldBeTrue();
                generations.Add(first);
                return builder =>
                {
                    builder.AddContent(
                        0,
                        Headless.Field(edit.Path.Number, captured => numberField = captured)
                    );
                    builder.AddContent(
                        1,
                        Headless.Field(edit.Path.Customer.Name, captured => nameField = captured)
                    );
                };
            }
        );

        using var tree = await Headless.Tree.CreateAsync(fragment);

        numberField.ShouldNotBeNull();
        nameField.ShouldNotBeNull();
        numberField!.IsChanged.ShouldBeFalse();
        nameField!.IsChanged.ShouldBeFalse();
        generations.Count.ShouldBe(1);

        session.Observable.Number = "ORD-2";
        await tree.FlushAsync();

        numberField!.IsChanged.ShouldBeTrue();
        numberField.Value.ShouldBe("ORD-2");
        nameField!.IsChanged.ShouldBeFalse();
        generations.Count.ShouldBe(2);
        ReferenceEquals(generations[0], generations[1]).ShouldBeFalse();

        // A plain parameter refresh without edits reuses the generation.
        await tree.ShowAsync(fragment);
        generations.Count.ShouldBe(3);
        ReferenceEquals(generations[1], generations[2]).ShouldBeTrue();
    }

    [Test]
    public async Task InvalidateStartsANewGeneration()
    {
        var session = Order().CreateEditSession();
        var seen = new List<SparseChangeSetQueryView?>();

        var fragment = Headless.Edit(
            session,
            edit =>
            {
                edit.TryGetStateSnapshot(out var current, out var error);
                error.ShouldBeNull();
                current.ShouldNotBeNull();
                seen.Add(current);
                if (seen.Count == 2)
                {
                    // End the generation from inside the render: the next
                    // capture must start fresh.
                    edit.InvalidateStateSnapshot();
                    edit.TryGetStateSnapshot(out var next, out var nextError);
                    nextError.ShouldBeNull();
                    seen.Add(next);
                }
            },
            static _ => builder => { }
        );

        using var tree = await Headless.Tree.CreateAsync(fragment);
        seen.Count.ShouldBe(1);

        session.Observable.Number = "ORD-2";
        await tree.FlushAsync();

        seen.Count.ShouldBe(3);
        ReferenceEquals(seen[0], seen[1]).ShouldBeFalse();
        ReferenceEquals(seen[1], seen[2]).ShouldBeFalse();
    }
}
