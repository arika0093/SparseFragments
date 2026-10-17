using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// No-op acceptance cleanup (#315): a successful TryAcceptChanges with no
// pending changes must clear stale conflicts and notify the UI, while a real
// acceptance still reports exactly one Accepted event.
public sealed class ChildAcceptConflictRegressionTests
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

    private static RenderFragment ConflictProbe(
        ISparseEditSession<OrderDto> session,
        Action<ISparseChildEditContext<OrderCustomer, SparseConflict>> capture,
        Action<string> show
    ) =>
        builder =>
        {
            builder.OpenComponent<SparseSessionScope<OrderDto>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<OrderDto>.Session), session);
            builder.AddAttribute(
                2,
                nameof(SparseSessionScope<OrderDto>.ChildContent),
                CustomerProbe(OrderDto.SparsePath.Customer, capture, show)
            );
            builder.CloseComponent();
        };

    private static RenderFragment CustomerProbe<TPaths, TSession>(
        ISparseChildEditPath<OrderDto, OrderCustomer, TPaths, TSession> path,
        Action<ISparseChildEditContext<OrderCustomer, SparseConflict>> capture,
        Action<string> show
    )
        where TPaths : ISparseTypedPath<OrderCustomer, OrderCustomer>
        where TSession : ISparseEditSessionWithPaths<OrderCustomer, TPaths> =>
        builder =>
        {
            builder.OpenComponent<
                SparseChildEdit<OrderDto, OrderCustomer, TPaths, SparseConflict, TSession>
            >(0);
            builder.AddAttribute(
                1,
                nameof(
                    SparseChildEdit<OrderDto, OrderCustomer, TPaths, SparseConflict, TSession>.Path
                ),
                path
            );
            builder.AddAttribute(
                2,
                nameof(
                    SparseChildEdit<
                        OrderDto,
                        OrderCustomer,
                        TPaths,
                        SparseConflict,
                        TSession
                    >.ChildContent
                ),
                (RenderFragment<
                    SparseChildEditContext<OrderCustomer, TPaths, SparseConflict, TSession>
                >)(
                    context =>
                        probeBuilder =>
                        {
                            capture(context);
                            var text = context.Conflicts is null
                                ? "none"
                                : context.Conflicts.Count.ToString();
                            show(text);
                            probeBuilder.AddContent(0, text);
                        }
                )
            );
            builder.CloseComponent();
        };

    [Test]
    public async Task NoOpAcceptClearsStaleConflictsAndRefreshesUi()
    {
        var parent = Order().CreateEditSession();
        ISparseChildEditContext<OrderCustomer, SparseConflict>? captured = null;
        var shown = string.Empty;
        using var tree = await Headless.Tree.CreateAsync(
            ConflictProbe(parent, context => captured = context, text => shown = text)
        );
        var child = captured.ShouldNotBeNull();
        var acceptedCount = 0;
        child.Accepted += () => acceptedCount++;

        // Same-path edits on both sides fail to merge with structured
        // conflicts, and the conflict UI shows them.
        ((OrderCustomer.EditSession)child.Session)
            .Observable
            .Name = "Child";
        parent.Observable.Customer!.Name = "Parent";
        child.TryAcceptChanges(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        child.Conflicts.ShouldNotBeNull();

        // A failed merge queues no render by itself, so touch an unrelated
        // member to surface the conflict UI, then prove the stale entry
        // survives an Undo render until the acceptance clears it.
        parent.Observable.Number = "ORD-2";
        await tree.FlushAsync();
        shown.ShouldBe("1");

        // Undoing the draft back to clean, then accepting, succeeds with null
        // out-conflicts, clears the stale list, and stays silent on Accepted
        // because nothing merged.
        child.Undo();
        await tree.FlushAsync();
        shown.ShouldBe("1");
        child.Conflicts.ShouldNotBeNull();

        // The no-op acceptance clears the stale conflicts; the UI clears on
        // the render its notification queues, without discarding the child.
        child.TryAcceptChanges(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        child.Conflicts.ShouldBeNull();
        acceptedCount.ShouldBe(0);
        await tree.FlushAsync();
        shown.ShouldBe("none");

        // A later real acceptance still reports exactly one Accepted event.
        ((OrderCustomer.EditSession)child.Session)
            .Observable
            .Email = "child@example.com";
        child.TryAcceptChanges(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        acceptedCount.ShouldBe(1);
        await tree.FlushAsync();
        shown.ShouldBe("none");
    }

    [Test]
    public async Task FailedAcceptWithoutStructuredConflictsKeepsInvalidationIntact()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(parent);
        var scope = host.ScopeContext;
        var path = OrderDto.SparsePath.Lines.Key("a");
        var child = scope.OpenChildSession(parent, path);
        ((OrderLine.EditSession)child.Session).Observable.Quantity = 9;

        // The missing target fails without structured conflicts and detaches
        // the pending draft instead of disposing it.
        parent.Observable.Lines.RemoveAt(0);
        await host.FlushAsync();
        child.TryAcceptChanges(out var conflicts).ShouldBeFalse();
        conflicts.ShouldBeNull();
        scope.GetDetachedChildPaths().ShouldContain(path.UntypedPath);

        // Restoring the draft to clean and accepting succeeds, clears
        // conflicts, notifies the UI, and leaves the detached draft held.
        var notifications = 0;
        void OnChanged() => notifications++;
        scope.Changed += OnChanged;
        child.Undo();
        child.TryAcceptChanges(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        child.Conflicts.ShouldBeNull();
        scope.Changed -= OnChanged;
        notifications.ShouldBe(1);
        scope.GetDetachedChildPaths().ShouldContain(path.UntypedPath);
        scope.TryGetDetachedChild(parent, path, out var recovered).ShouldBeTrue();
        recovered.ShouldBeSameAs(child);

        // The target is still gone: reopening the path keeps failing instead
        // of resurrecting a stale session.
        Should.Throw<ArgumentException>(() => scope.OpenChildSession(parent, path));
        scope.HasChanges.ShouldBeFalse();
    }
}
