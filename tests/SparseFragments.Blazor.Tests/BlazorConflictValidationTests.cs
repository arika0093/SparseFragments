using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor.Tests;

public sealed class BlazorConflictValidationTests
{
    private static T Raw<T>(ISparseEditSession<T> session)
        where T : class => session.Model;

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
            Contacts = new() { ["billing"] = new OrderCustomer { Name = "Ada" } },
        };

    private static SparseConflict Conflict(SparsePath path, string? reason = null) =>
        new(
            path,
            SparseConflictKind.Scalar,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            reason
        );

    [Test]
    public void ScalarReloadConflictMapsToField()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var notified = 0;
        context.OnValidationStateChanged += (_, _) => notified++;

        Raw(session).Number = "LOCAL";
        var server = Order();
        server.Number = "SERVER";
        var reload = session.Reload(server);
        reload.HasConflicts.ShouldBeTrue();
        reload.Conflicts.Single().PathText.ShouldBe(nameof(OrderDto.Number));

        var added = session.AddConflicts(store, context, reload.Conflicts);
        added.ShouldBe(1);
        context
            .GetValidationMessages(session.Field(nameof(OrderDto.Number)))
            .Single()
            .ShouldContain("Conflict at 'Number'");
        context.GetValidationMessages().ShouldNotBeEmpty();
        notified.ShouldBe(1);
    }

    [Test]
    public void NestedKeyedAndDictionaryConflictsMapToFields()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var nested = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Customer))
            .Member(nameof(OrderCustomer.Name));
        var keyed = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Lines))
            .Key("b")
            .Member(nameof(OrderLine.Quantity));
        var dictionary = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Contacts))
            .Key("billing")
            .Member(nameof(OrderCustomer.Name));

        var added = session.AddConflicts(
            store,
            context,
            [Conflict(nested), Conflict(keyed), Conflict(dictionary)]
        );
        added.ShouldBe(3);

        context
            .GetValidationMessages(session.Field("Customer.Name"))
            .Single()
            .ShouldContain("Customer.Name");
        context
            .GetValidationMessages(session.Field("Lines[\"b\"].Quantity"))
            .Single()
            .ShouldContain("Lines[\"b\"].Quantity");
        context
            .GetValidationMessages(session.Field("Contacts[\"billing\"].Name"))
            .Single()
            .ShouldContain("Contacts[\"billing\"].Name");
    }

    [Test]
    public void RemovedKeyFallsBackToNearestParent()
    {
        var session = Order().CreateEditSession();
        Raw(session).Lines.RemoveAll(line => line.Sku == "a");
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var removed = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Lines))
            .Key("a")
            .Member(nameof(OrderLine.Quantity));
        Should.Throw<ArgumentException>(() => session.Field(removed));

        var added = session.AddConflicts(store, context, [Conflict(removed)]);
        added.ShouldBe(1);
        context.GetValidationMessages().ShouldNotBeEmpty();
        context.GetValidationMessages(session.Field(nameof(OrderDto.Lines))).ShouldNotBeEmpty();
    }

    [Test]
    public void RemovedKeyModelFallbackSurfacesAtModelLevel()
    {
        var session = Order().CreateEditSession();
        Raw(session).Lines.RemoveAll(line => line.Sku == "a");
        var model = Raw(session);
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var options = new SparseConflictValidationOptions
        {
            Fallback = SparseConflictFallbackMode.Model,
        };

        var removed = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Lines))
            .Key("a")
            .Member(nameof(OrderLine.Quantity));

        session.AddConflicts(store, context, [Conflict(removed)], options).ShouldBe(1);
        context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldNotBeEmpty();
    }

    [Test]
    public void MissingNestedModelFallsBackToParent()
    {
        var session = Order().CreateEditSession();
        Raw(session).Customer = null!;
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var nested = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Customer))
            .Member(nameof(OrderCustomer.Name));
        Should.Throw<ArgumentException>(() => session.Field(nested));

        session.AddConflicts(store, context, [Conflict(nested)]).ShouldBe(1);
        context.GetValidationMessages(session.Field(nameof(OrderDto.Customer))).ShouldNotBeEmpty();
    }

    [Test]
    public void RootAndForeignPathsSurfaceAtModelLevel()
    {
        var session = Order().CreateEditSession();
        var model = Raw(session);
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var modelField = new FieldIdentifier(model, string.Empty);

        var root = SparsePath.Root<OrderDto>();
        var foreign = SparsePath.Root<Team>().Member(nameof(Team.Name));

        session
            .AddConflicts(
                store,
                context,
                [
                    new SparseConflict(
                        root,
                        SparseConflictKind.WholeContribution,
                        Optional<object?>.Missing,
                        Optional<object?>.Missing,
                        Optional<object?>.Missing,
                        null
                    ),
                    Conflict(foreign),
                ]
            )
            .ShouldBe(2);
        context.GetValidationMessages(modelField).Count().ShouldBe(2);
    }

    [Test]
    public void ConflictStoreStaysDistinctFromManualMessages()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var conflictStore = session.CreateValidationStore(context);
        var manualStore = session.CreateValidationStore(context);

        var options = new SparseConflictValidationOptions
        {
            MessageFactory = static conflict => $"CONFLICT:{conflict.PathText}",
        };
        session.AddConflicts(
            conflictStore,
            context,
            [Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number)))],
            options
        );
        manualStore.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");

        context
            .GetValidationMessages(session.Field(nameof(OrderDto.Number)))
            .ShouldContain("CONFLICT:Number");
        context
            .GetValidationMessages(session.Field(nameof(OrderDto.Number)))
            .ShouldContain("Number is required.");

        session.ClearConflicts(conflictStore, context);
        var remaining = context.GetValidationMessages(session.Field(nameof(OrderDto.Number)));
        remaining.ShouldContain("Number is required.");
        remaining.ShouldNotContain("CONFLICT:Number");
    }

    [Test]
    public void PreserveClearAndRepeatedValidation()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var conflicts = new[]
        {
            Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number))),
        };

        session.AddConflicts(store, context, conflicts).ShouldBe(1);
        session.AddConflicts(store, context, conflicts).ShouldBe(1);
        context.GetValidationMessages().Count().ShouldBe(2);

        var clearing = new SparseConflictValidationOptions { ClearStoreFirst = true };
        session.AddConflicts(store, context, conflicts, clearing).ShouldBe(1);
        context.GetValidationMessages().Count().ShouldBe(1);

        session.ClearConflicts(store, context);
        context.GetValidationMessages().ShouldBeEmpty();
    }

    [Test]
    public void ReplacedStoreStartsFreshAfterClearingOld()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var oldStore = session.CreateValidationStore(context);
        var conflicts = new[]
        {
            Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number))),
        };

        session.AddConflicts(oldStore, context, conflicts).ShouldBe(1);
        session.ClearConflicts(oldStore, context);

        var freshStore = session.CreateValidationStore(context);
        session.AddConflicts(freshStore, context, conflicts).ShouldBe(1);
        context.GetValidationMessages().Count().ShouldBe(1);
    }

    [Test]
    public void FreshServerStateReplacesContextAndRejectsStale()
    {
        var session = Order().CreateEditSession();
        var staleContext = session.CreateEditContext();
        var staleStore = session.CreateValidationStore(staleContext);
        void Validate(object? sender, ValidationRequestedEventArgs args) => staleStore.Clear();
        staleContext.OnValidationRequested += Validate;

        var persisted = Order();
        persisted.Number = "ORD-2";
        staleContext.OnValidationRequested -= Validate;
        var freshSession = persisted.CreateEditSession();
        var freshContext = freshSession.ReplaceEditContext(staleContext);
        var freshStore = freshSession.CreateValidationStore(freshContext);

        ReferenceEquals(freshContext.Model, persisted).ShouldBeTrue();
        ReferenceEquals(staleContext.Model, Raw(session)).ShouldBeTrue();

        var conflicts = new[]
        {
            Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number))),
        };
        Should.Throw<ArgumentException>(() =>
            freshSession.AddConflicts(staleStore, staleContext, conflicts)
        );
        Should.Throw<ArgumentException>(() =>
            session.AddConflicts(freshStore, freshContext, conflicts)
        );
        freshSession.AddConflicts(freshStore, freshContext, conflicts).ShouldBe(1);
        freshContext.GetValidationMessages().ShouldNotBeEmpty();

        Should.Throw<ArgumentException>(() =>
            freshSession.ReplaceEditContext(freshSession.CreateEditContext())
        );
    }

    [Test]
    public void ExistingValidationHelpersKeepWorkingAlongsideConflicts()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        session.AddValidationError(
            store,
            session.Field(nameof(OrderDto.Number)),
            "Server rejected the order number."
        );
        session
            .AddConflicts(
                store,
                context,
                [Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number)))]
            )
            .ShouldBe(1);

        var messages = context.GetValidationMessages(session.Field(nameof(OrderDto.Number)));
        messages.ShouldContain("Server rejected the order number.");
        messages.ShouldContain(m => m.Contains("Conflict at 'Number'"));
    }
}
