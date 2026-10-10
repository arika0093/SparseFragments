using System.Net.Http;
using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor.Tests;

// Deterministic counterpart of docs/how-to/blazor-edit-form.md (#211).
// Exercises the full form lifecycle against OrderDto: Observable input with
// EditContext field tracking, validation failure, successful save with
// server normalisation, conflict display with retain-and-retry, and
// transport failure without baseline movement.
public sealed class BlazorEditFormFlowTests
{
    private abstract record SaveResult;

    private sealed record Saved : SaveResult
    {
        public OrderDto Persisted { get; init; } = new();
    }

    private sealed record Conflicted : SaveResult
    {
        public OrderDto ServerState { get; init; } = new();
    }

    private sealed class FakeSaveService
    {
        public List<OrderDto.ChangePayload> Received { get; } = new();

        public SaveResult NextResult { get; set; } = new Saved();

        public bool ThrowTransportError { get; set; }

        public async Task<SaveResult> SaveAsync(
            OrderDto.ChangePayload payload,
            CancellationToken cancellationToken
        )
        {
            Received.Add(payload);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowTransportError)
            {
                throw new HttpRequestException("The server did not respond.");
            }

            return NextResult;
        }
    }

    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
        };

    [Test]
    public void ObservableEditMarksFieldModifiedAndValidates()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        context.OnValidationRequested += (_, _) =>
        {
            store.Clear();
            if (string.IsNullOrWhiteSpace(session.Current.Number))
            {
                store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        };
        void Edit(string value)
        {
            session.Observable.Number = value;
            context.NotifyFieldChanged(session.Field(nameof(OrderDto.Number)));
        }

        Edit("ORD-2");
        context.IsModified().ShouldBeTrue();
        context.Validate().ShouldBeTrue();

        Edit(string.Empty);
        context.Validate().ShouldBeFalse();
        context.GetValidationMessages().ShouldContain("Number is required.");
    }

    [Test]
    public async Task SuccessfulSaveAdoptsPersistedState()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        void Validate(object? sender, ValidationRequestedEventArgs args)
        {
            store.Clear();
            if (string.IsNullOrWhiteSpace(session.Current.Number))
            {
                store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        }
        context.OnValidationRequested += Validate;
        var service = new FakeSaveService
        {
            NextResult = new Saved
            {
                Persisted = new OrderDto
                {
                    Number = "ORD-2",
                    Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                },
            },
        };

        session.Observable.Number = "ord-2";
        context.NotifyFieldChanged(session.Field(nameof(OrderDto.Number)));
        context.Validate().ShouldBeTrue();

        var isSaving = true;
        try
        {
            var submitted = session.CreateChangeSet();
            var result = await service.SaveAsync(submitted.ToPayload(), CancellationToken.None);
            var persisted = ((Saved)result).Persisted;
            context.OnValidationRequested -= Validate;
            session = persisted.CreateEditSession();
            context = session.CreateEditContext();
            persisted.Number.ShouldBe("ORD-2");
            session.HasChanges.ShouldBeFalse();
            ReferenceEquals(context.Model, persisted).ShouldBeTrue();
        }
        finally
        {
            isSaving = false;
        }

        isSaving.ShouldBeFalse();
        service.Received.Count.ShouldBe(1);
    }

    [Test]
    public void ConflictKeepsSessionAndResolvesExplicitly()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        context.OnValidationRequested += (_, _) =>
        {
            store.Clear();
            if (string.IsNullOrWhiteSpace(session.Current.Number))
            {
                store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        };
        var serverState = Order();
        serverState.Number = "SERVER";

        session.Observable.Number = "ORD-2";
        context.NotifyFieldChanged(session.Field(nameof(OrderDto.Number)));
        var submitted = session.CreateChangeSet();
        if (submitted.TryApplyTo(serverState, out _, out var conflicts))
        {
            throw new InvalidOperationException("Expected a semantic conflict.");
        }

        foreach (var conflict in conflicts)
        {
            session.AddValidationError(store, conflict.PathText, "Server kept a newer value.");
        }

        conflicts.Single().PathText.ShouldBe(nameof(OrderDto.Number));
        session.HasChanges.ShouldBeTrue();
        session.Current.Number.ShouldBe("ORD-2");

        var reload = session.Reload(serverState);
        reload.HasConflicts.ShouldBeTrue();
        session.Current.Number.ShouldBe("ORD-2");
        session.HasChanges.ShouldBeTrue();

        session.Observable.Number = "SERVER";
        context.NotifyFieldChanged(session.Field(nameof(OrderDto.Number)));
        var resolved = session.Reload(serverState);
        resolved.HasConflicts.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public async Task TransportFailureKeepsBaselineAndRetries()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        context.OnValidationRequested += (_, _) =>
        {
            store.Clear();
            if (string.IsNullOrWhiteSpace(session.Current.Number))
            {
                store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        };
        var service = new FakeSaveService { ThrowTransportError = true };

        session.Observable.Number = "ORD-2";
        context.NotifyFieldChanged(session.Field(nameof(OrderDto.Number)));

        var isSaving = true;
        var transportFailed = false;
        try
        {
            var submitted = session.CreateChangeSet();
            try
            {
                await service.SaveAsync(submitted.ToPayload(), CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                transportFailed = true;
            }
        }
        finally
        {
            isSaving = false;
        }

        transportFailed.ShouldBeTrue();
        isSaving.ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
        var retrySubmitted = session.CreateChangeSet();
        retrySubmitted.Number.Before.Value.ShouldBe("ORD-1");
        retrySubmitted.Number.After.Value.ShouldBe("ORD-2");

        service.ThrowTransportError = false;
        var retried = Order();
        retried.Number = "ORD-2";
        service.NextResult = new Saved { Persisted = retried };
        var retryResult = await service.SaveAsync(
            retrySubmitted.ToPayload(),
            CancellationToken.None
        );
        session = ((Saved)retryResult).Persisted.CreateEditSession();
        session.HasChanges.ShouldBeFalse();
        service.Received.Count.ShouldBe(2);
    }
}
