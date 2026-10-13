using Microsoft.EntityFrameworkCore;
using SparseFragments.Playground.Models;

namespace SparseFragments.Playground.Pages.Case5;

internal static class PlaygroundConcurrency
{
    internal static void SimulateConcurrentEdit(
        DbContextOptions<PlaygroundEfContext> options,
        int orderId
    )
    {
        using var otherUserDb = new PlaygroundEfContext(options);
        var otherUserOrder = otherUserDb.Orders.Single(order => order.Id == orderId);
        otherUserOrder.CustomerName = $"{otherUserOrder.CustomerName} (edited by another user)";
        otherUserOrder.Revision++;
        otherUserDb.SaveChanges();
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
}
