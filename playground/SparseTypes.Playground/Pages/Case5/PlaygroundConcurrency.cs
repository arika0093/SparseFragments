using Microsoft.EntityFrameworkCore;
using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case5;

internal static class PlaygroundConcurrency
{
    internal static void SimulateConcurrentEdit(
        DbContextOptions<PlaygroundEfContext> options,
        int orderId
    )
    {
        using var otherUserDb = new PlaygroundEfContext(options);
        var otherUserOrder = otherUserDb.Orders.Single(order => order.Id == orderId);
        otherUserOrder.CustomerName = PlaygroundRandom.CreateDifferentCustomerName(
            otherUserOrder.CustomerName
        );
        otherUserOrder.Revision++;
        otherUserDb.SaveChanges();
    }

    internal static ConcurrentUpdate SimulatePeriodicUpdate(
        DbContextOptions<PlaygroundEfContext> options,
        int orderId
    )
    {
        using var otherUserDb = new PlaygroundEfContext(options);
        var order = otherUserDb
            .Orders.Include(static item => item.Note)
            .Single(item => item.Id == orderId);
        var field =
            Random.Shared.Next(2) == 0 ? ConcurrentOrderField.Customer : ConcurrentOrderField.Notes;
        var before =
            field == ConcurrentOrderField.Customer
                ? order.CustomerName
                : order.Note?.Text ?? string.Empty;
        var after =
            field == ConcurrentOrderField.Customer
                ? PlaygroundRandom.CreateDifferentCustomerName(order.CustomerName)
                : $"Concurrent note {order.Revision + 1}";
        while (string.Equals(before, after, StringComparison.Ordinal))
        {
            after += "*";
        }

        SetValue(order, field, after);
        order.Revision++;
        otherUserDb.SaveChanges();
        return new ConcurrentUpdate
        {
            Property = field == ConcurrentOrderField.Customer ? "Customer" : "Notes",
            Before = before,
            After = after,
            Revision = order.Revision,
        };
    }

    internal static ConcurrentUpdate ApplyControlledUpdate(
        DbContextOptions<PlaygroundEfContext> options,
        int orderId,
        ConcurrentOrderField field,
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        using var otherUserDb = new PlaygroundEfContext(options);
        var order = otherUserDb
            .Orders.Include(static item => item.Note)
            .Single(item => item.Id == orderId);
        var before =
            field == ConcurrentOrderField.Customer
                ? order.CustomerName
                : order.Note?.Text ?? string.Empty;
        if (string.Equals(before, value, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The concurrent value must differ from the current value.",
                nameof(value)
            );
        }

        SetValue(order, field, value);
        order.Revision++;
        otherUserDb.SaveChanges();
        return new ConcurrentUpdate
        {
            Property = field == ConcurrentOrderField.Customer ? "Customer" : "Notes",
            Before = before,
            After = value,
            Revision = order.Revision,
        };
    }

    internal static void EnsureCurrentRevision(
        DbContextOptions<PlaygroundEfContext> options,
        int orderId,
        int expectedRevision
    )
    {
        using var currentDb = new PlaygroundEfContext(options);
        var currentRevision = currentDb
            .Orders.Where(order => order.Id == orderId)
            .Select(order => order.Revision)
            .Single();
        if (currentRevision != expectedRevision)
        {
            throw new DbUpdateConcurrencyException(
                $"Order {orderId} changed from revision {expectedRevision} to {currentRevision}."
            );
        }
    }

    private static void SetValue(
        PlaygroundOrderEntity order,
        ConcurrentOrderField field,
        string value
    )
    {
        if (field == ConcurrentOrderField.Customer)
        {
            order.CustomerName = value;
            return;
        }

        if (order.Note is null)
        {
            order.Note = new PlaygroundNoteEntity();
        }

        order.Note.Text = value;
    }
}

internal enum ConcurrentOrderField
{
    Customer,
    Notes,
}

internal sealed class ConcurrentUpdate
{
    public required string Property { get; init; }

    public required string Before { get; init; }

    public required string After { get; init; }

    public required int Revision { get; init; }
}
