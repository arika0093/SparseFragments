namespace SparseTypes.Blazor.Tests;

// PassThrough narrow invalidation (#313): unrelated parent edits retain clean
// children on their existing sessions; only the affected subtree invalidates.
public sealed class ScopePassThroughRetentionTests
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
    public async Task RepeatedUnrelatedRootEditsRetainEveryCleanChild()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(
            parent,
            mode: SparseChildEditMode.PassThrough
        );
        var scope = host.ScopeContext;
        var customer = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var first = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("a"));
        var second = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("b"));
        var customerSession = customer.Session;
        var firstSession = first.Session;
        var secondSession = second.Session;
        var notifications = 0;
        void OnChanged() => notifications++;
        scope.Changed += OnChanged;

        for (var edit = 0; edit < 10; edit++)
        {
            parent.Observable.Number = $"ORD-{edit + 2}";
        }

        await host.FlushAsync();

        scope.Changed -= OnChanged;
        notifications.ShouldBe(0);
        ReferenceEquals(customerSession, customer.Session).ShouldBeTrue();
        ReferenceEquals(firstSession, first.Session).ShouldBeTrue();
        ReferenceEquals(secondSession, second.Session).ShouldBeTrue();
        customerSession.Observable.Name.ShouldBe("Ada");
        ((OrderLine.EditSession)firstSession).Observable.Quantity.ShouldBe(1);
        ((OrderLine.EditSession)secondSession).Observable.Quantity.ShouldBe(2);
        parent.HasPendingChildSessions.ShouldBeFalse();

        // A retained child still merges onto the moved parent without a
        // refork: the rebase absorbs the unrelated drift.
        customerSession.Observable.Name = "Grace";
        await host.FlushAsync();
        parent.Observable.Customer!.Name.ShouldBe("Grace");
    }

    [Test]
    public async Task SameSubtreeParentWriteInvalidatesOnlyTheAffectedChild()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(
            parent,
            mode: SparseChildEditMode.PassThrough
        );
        var scope = host.ScopeContext;
        var customer = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var line = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("a"));
        var lineSession = line.Session;
        var notifications = 0;
        void OnChanged() => notifications++;
        scope.Changed += OnChanged;

        parent.Observable.Customer!.Name = "Parent";
        await host.FlushAsync();

        scope.Changed -= OnChanged;
        notifications.ShouldBe(1);
        Should.Throw<ObjectDisposedException>(() => customer.Session);
        ReferenceEquals(lineSession, line.Session).ShouldBeTrue();
        ((OrderLine.EditSession)lineSession).Observable.Quantity.ShouldBe(1);

        // The invalidated path reopens on the fresh parent state.
        var reopened = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        reopened.ShouldNotBeSameAs(customer);
        reopened.Session.Observable.Name.ShouldBe("Parent");
    }

    [Test]
    public async Task SiblingKeyedRowEditRetainsTheUntouchedRow()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(
            parent,
            mode: SparseChildEditMode.PassThrough
        );
        var scope = host.ScopeContext;
        var first = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("a"));
        var second = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("b"));
        var firstSession = first.Session;

        parent.Observable.Lines[1].Quantity = 9;
        await host.FlushAsync();

        ReferenceEquals(firstSession, first.Session).ShouldBeTrue();
        ((OrderLine.EditSession)firstSession).Observable.Quantity.ShouldBe(1);
        Should.Throw<ObjectDisposedException>(() => second.Session);
        parent.Observable.Lines[1].Quantity.ShouldBe(9);
    }

    [Test]
    public async Task UndoAndRedoDisposeAndRebindCleanChildren()
    {
        var model = Order();
        var parent = model.CreateEditSession();
        var history = parent.EnableHistory();
        using var host = await ScopeTestHost.CreateAsync(
            parent,
            mode: SparseChildEditMode.PassThrough
        );
        var scope = host.ScopeContext;
        var customer = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var line = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("a"));
        var customerSession = customer.Session;
        var lineSession = line.Session;

        // An unrelated edit retains clean children.
        parent.Observable.Number = "ORD-2";
        await host.FlushAsync();
        ReferenceEquals(customerSession, customer.Session).ShouldBeTrue();
        ReferenceEquals(lineSession, line.Session).ShouldBeTrue();

        // Undo rewrites through the fragment writer, which replaces nested
        // instances even for unrelated subtrees, so children dispose and the
        // subtree rebinds on reopen instead of stranding stale forks.
        history.Undo();
        await host.FlushAsync();
        model.Number.ShouldBe("ORD-1");
        Should.Throw<ObjectDisposedException>(() => customer.Session);
        Should.Throw<ObjectDisposedException>(() => line.Session);
        scope
            .OpenChildSession(parent, OrderDto.SparsePath.Customer)
            .Session.Observable.Name.ShouldBe("Ada");

        history.CanRedo.ShouldBeTrue();
        history.Redo();
        await host.FlushAsync();
        model.Number.ShouldBe("ORD-2");
        scope
            .OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("a"))
            .Session.Observable.Quantity.ShouldBe(1);
    }

    [Test]
    public async Task ParentTargetReplacementInvalidatesOnlyTheAffectedChild()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(
            parent,
            mode: SparseChildEditMode.PassThrough
        );
        var scope = host.ScopeContext;
        var customerContext = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var line = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("a"));
        var lineSession = line.Session;

        parent.Descriptors.TryGet(nameof(OrderDto.Customer), out var descriptor).ShouldBeTrue();
        descriptor
            .TrySetValue(new OrderCustomer { Name = "Grace", Email = "grace@example.com" })
            .ShouldBeTrue();
        await host.FlushAsync();

        Should.Throw<ObjectDisposedException>(() => customerContext.Session);
        ReferenceEquals(lineSession, line.Session).ShouldBeTrue();
        scope
            .OpenChildSession(parent, OrderDto.SparsePath.Customer)
            .Session.Observable.Name.ShouldBe("Grace");
    }

    [Test]
    public async Task PassThroughChildWriteFollowedByUnrelatedEditStaysCorrect()
    {
        var model = Order();
        var parent = model.CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(
            parent,
            mode: SparseChildEditMode.PassThrough
        );
        var scope = host.ScopeContext;
        var child = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var acceptedCount = 0;
        child.Accepted += () => acceptedCount++;

        child.Session.Observable.Name = "Grace";
        await host.FlushAsync();
        model.Customer.Name.ShouldBe("Grace");
        acceptedCount.ShouldBe(1);

        // The accepted edit is parent dirt now, so the next unrelated edit
        // conservatively reopens the child instead of risking a stale draft
        // across a later revert. Values stay correct either way.
        parent.Observable.Number = "ORD-2";
        await host.FlushAsync();

        model.Customer.Name.ShouldBe("Grace");
        model.Number.ShouldBe("ORD-2");
        var current = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        current.Session.Observable.Name.ShouldBe("Grace");
        scope.HasChanges.ShouldBeFalse();
    }

    [Test]
    public async Task ModeChangeKeepsContextsAndDisposalStillReleases()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(parent);
        var scope = host.ScopeContext;
        var customer = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var line = scope.OpenChildSession(parent, OrderDto.SparsePath.Lines.Key("a"));

        await host.SetModeAsync(SparseChildEditMode.PassThrough);

        scope.Mode.ShouldBe(SparseChildEditMode.PassThrough);
        customer.Mode.ShouldBe(SparseChildEditMode.PassThrough);
        line.Mode.ShouldBe(SparseChildEditMode.PassThrough);

        var customerSession = customer.Session;
        parent.Observable.Number = "ORD-2";
        await host.FlushAsync();
        ReferenceEquals(customerSession, customer.Session).ShouldBeTrue();

        await host.SetModeAsync(SparseChildEditMode.Draft);
        customer.Mode.ShouldBe(SparseChildEditMode.Draft);
        customerSession.Observable.Name.ShouldBe("Ada");
    }
}
