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

        await tree.ShowAsync(fragment);
        generations.Count.ShouldBe(2);
        ReferenceEquals(generations[0], generations[1]).ShouldBeTrue();

        session.Observable.Number = "ORD-2";
        await tree.FlushAsync();

        numberField!.IsChanged.ShouldBeTrue();
        numberField.Value.ShouldBe("ORD-2");
        nameField!.IsChanged.ShouldBeFalse();
        generations.Count.ShouldBe(3);
        ReferenceEquals(generations[1], generations[2]).ShouldBeFalse();

        // A plain parameter refresh without edits reuses the generation.
        await tree.ShowAsync(fragment);
        generations.Count.ShouldBe(4);
        ReferenceEquals(generations[2], generations[3]).ShouldBeTrue();
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

    [Test]
    public async Task ParentParameterRefreshCapturesRawScalarEditForAllFields()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, string>? numberField = null;
        SparseFieldContext<OrderDto, string>? nameField = null;
        var hasChanges = false;
        var generations = new List<SparseChangeSetQueryView?>();
        var changes = new List<ISparseChangeSet?>();

        RenderFragment Fragment() =>
            Headless.Edit(
                session,
                edit =>
                {
                    hasChanges = edit.HasChanges;
                    edit.TryGetStateSnapshot(out var first, out var firstError);
                    edit.TryGetStateSnapshot(out var second, out var secondError);
                    firstError.ShouldBeNull();
                    secondError.ShouldBeNull();
                    ReferenceEquals(first, second).ShouldBeTrue();
                    generations.Add(first);
                    changes.Add(first!.Changes);
                },
                edit =>
                    builder =>
                    {
                        builder.AddContent(
                            0,
                            Headless.Field(edit.Path.Number, captured => numberField = captured)
                        );
                        builder.AddContent(
                            1,
                            Headless.Field(
                                edit.Path.Customer.Name,
                                captured => nameField = captured
                            )
                        );
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(Fragment());
        generations.Count.ShouldBe(1);
        generations[0]!.ProjectionCount.ShouldBe(1);

        live.Number = "ORD-2";
        session.FieldState(OrderDto.SparsePath.Number).IsChanged.ShouldBeTrue();
        await tree.ShowAsync(Fragment());

        generations.Count.ShouldBe(2);
        ReferenceEquals(generations[0], generations[1]).ShouldBeFalse();
        ReferenceEquals(changes[1], generations[1]!.Changes).ShouldBeTrue();
        generations[1]!.ProjectionCount.ShouldBe(1);
        hasChanges.ShouldBeTrue();
        numberField!.IsChanged.ShouldBeTrue();
        numberField.Value.ShouldBe("ORD-2");
        nameField!.IsChanged.ShouldBeFalse();
    }

    [Test]
    public async Task ParentParameterRefreshCapturesNestedReplacementForAllFields()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, string>? nameField = null;
        SparseFieldContext<OrderDto, string?>? emailField = null;
        var hasChanges = false;
        var generations = new List<SparseChangeSetQueryView?>();

        RenderFragment Fragment() =>
            Headless.Edit(
                session,
                edit =>
                {
                    hasChanges = edit.HasChanges;
                    edit.TryGetStateSnapshot(out var first, out var firstError);
                    edit.TryGetStateSnapshot(out var second, out var secondError);
                    firstError.ShouldBeNull();
                    secondError.ShouldBeNull();
                    ReferenceEquals(first, second).ShouldBeTrue();
                    generations.Add(first);
                },
                edit =>
                    builder =>
                    {
                        builder.AddContent(
                            0,
                            Headless.Field(
                                edit.Path.Customer.Name,
                                captured => nameField = captured
                            )
                        );
                        builder.AddContent(
                            1,
                            Headless.Field(
                                edit.Path.Customer.Email,
                                captured => emailField = captured
                            )
                        );
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(Fragment());
        var previous = generations[0];

        live.Customer = new OrderCustomer { Name = "Grace", Email = "grace@example.com" };
        session.FieldState(OrderDto.SparsePath.Customer.Name).After.Value.ShouldBe("Grace");
        await tree.ShowAsync(Fragment());

        generations.Count.ShouldBe(2);
        ReferenceEquals(previous, generations[1]).ShouldBeFalse();
        generations[1]!.ProjectionCount.ShouldBe(1);
        hasChanges.ShouldBeTrue();
        nameField!.IsChanged.ShouldBeTrue();
        nameField.Value.ShouldBe("Grace");
        emailField!.IsChanged.ShouldBeTrue();
        emailField.Value.ShouldBe("grace@example.com");
    }

    [Test]
    public async Task ParentParameterRefreshCapturesKeyedReorderAndDeleteForAllFields()
    {
        var live = Order();
        live.Lines.Add(
            new OrderLine
            {
                Sku = "b",
                Quantity = 2,
                Price = 20m,
            }
        );
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, List<OrderLine>>? linesField = null;
        SparseFieldContext<OrderDto, int>? keyedQuantityField = null;
        SparseFieldContext<OrderDto, string>? numberField = null;
        SparseCollectionState? collectionState = null;
        var hasChanges = false;
        var generations = new List<SparseChangeSetQueryView?>();

        RenderFragment Fragment() =>
            Headless.Edit(
                session,
                edit =>
                {
                    hasChanges = edit.HasChanges;
                    edit.TryGetStateSnapshot(out var first, out var firstError);
                    edit.TryGetStateSnapshot(out var second, out var secondError);
                    firstError.ShouldBeNull();
                    secondError.ShouldBeNull();
                    ReferenceEquals(first, second).ShouldBeTrue();
                    generations.Add(first);
                },
                edit =>
                    builder =>
                    {
                        builder.AddContent(
                            0,
                            Headless.Field(
                                edit.Path.Lines,
                                captured =>
                                {
                                    linesField = captured;
                                    captured.TryGetCollectionState(out collectionState);
                                }
                            )
                        );
                        builder.AddContent(
                            1,
                            Headless.Field(
                                edit.Path.Lines.Key("a").Quantity,
                                captured => keyedQuantityField = captured
                            )
                        );
                        builder.AddContent(
                            2,
                            Headless.Field(edit.Path.Number, captured => numberField = captured)
                        );
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(Fragment());
        var rawModel = ((ISparseEditSession<OrderDto>)session).Model;
        rawModel.Lines = rawModel.Lines.AsEnumerable().Reverse().ToList();
        session.HasChanges.ShouldBeTrue();
        await tree.ShowAsync(Fragment());

        generations.Count.ShouldBe(2);
        ReferenceEquals(generations[0], generations[1]).ShouldBeFalse();
        generations[1]!.ProjectionCount.ShouldBe(1);
        hasChanges.ShouldBeTrue();
        linesField!.Value.Select(line => line.Sku).ShouldBe(["b", "a"]);
        collectionState!.Value.GetItemState("a").IsReordered.ShouldBeTrue();
        keyedQuantityField!.Value.ShouldBe(1);
        keyedQuantityField.IsChanged.ShouldBeFalse();
        numberField!.IsChanged.ShouldBeFalse();

        rawModel.Lines.RemoveAll(line => line.Sku == "b");
        await tree.ShowAsync(Fragment());

        generations.Count.ShouldBe(3);
        ReferenceEquals(generations[1], generations[2]).ShouldBeFalse();
        generations[2]!.ProjectionCount.ShouldBe(1);
        hasChanges.ShouldBeTrue();
        linesField!.Value.Select(line => line.Sku).ShouldBe(["a"]);
        collectionState!.Value.GetItemState("b").IsRemoved.ShouldBeTrue();
        keyedQuantityField!.Value.ShouldBe(1);
        numberField!.IsChanged.ShouldBeFalse();
    }
}
