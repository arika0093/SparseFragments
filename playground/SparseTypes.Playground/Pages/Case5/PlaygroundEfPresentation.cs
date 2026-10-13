using Microsoft.EntityFrameworkCore;
using SparseTypes;
using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case5;

internal static class PlaygroundEfPresentation
{
    internal static List<(
        string Entity,
        string State,
        string Property,
        string Before,
        string After
    )> CollectModifiedRows(PlaygroundEfContext db)
    {
        var rows =
            new List<(string Entity, string State, string Property, string Before, string After)>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (
                entry.State
                is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
            )
            {
                continue;
            }

            var name = entry.Entity.GetType().Name;
            var key = string.Join(
                ",",
                entry
                    .Properties.Where(static property => property.Metadata.IsPrimaryKey())
                    .Select(static property =>
                        PlaygroundOrderJson.FormatValue(property.CurrentValue)
                    )
            );
            if (entry.State == EntityState.Added)
            {
                var values = string.Join(
                    ", ",
                    entry.Properties.Select(static property =>
                        $"{property.Metadata.Name}={PlaygroundOrderJson.FormatValue(property.CurrentValue)}"
                    )
                );
                rows.Add((name, "Added", $"#{key}", "—", values));
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                rows.Add((name, "Deleted", $"#{key}", $"#{key}", "—"));
                continue;
            }

            foreach (var property in entry.Properties.Where(static property => property.IsModified))
            {
                rows.Add(
                    (
                        name,
                        "Modified",
                        property.Metadata.Name,
                        PlaygroundOrderJson.FormatValue(property.OriginalValue),
                        PlaygroundOrderJson.FormatValue(property.CurrentValue)
                    )
                );
            }
        }

        return rows;
    }

    // Audit (#256): the ChangeSet save-preview table projects through
    // SparseChangeRowProjection with the shared PlaygroundPageHelper operation
    // mapping, so the per-model ChangeKind switch and the CSS class helper that
    // duplicated it are gone. EfStateOperation stays: EntityState rows are EF
    // entries, not change records, so they keep their own mapping.
    // FormatChangeValue stays too: rebase conflict endpoints are SparseConflict
    // values, a different record type the row projection does not cover.
    // Project stays here as well: the Case 5 page maps the tracked entity to its
    // DTO in several lifecycle steps (seed, save acknowledgement, conflict
    // recovery), and the projection is presentation, not mapping.
    internal static string FormatChangeValue(Optional<object?> value) =>
        value.IsPresent ? PlaygroundOrderJson.FormatValue(value.Value) : "—";

    internal static string EfStateOperation(string state) =>
        state switch
        {
            "Added" => "Add",
            "Deleted" => "Remove",
            "Modified" => "Edit",
            _ => state,
        };

    /// <summary>Projects the tracked order entity to its DTO in store order.</summary>
    /// <param name="order">The tracked entity, with lines loaded.</param>
    /// <returns>The DTO the edit session owns and diffs.</returns>
    internal static PlaygroundOrderDto Project(PlaygroundOrderEntity order) =>
        new()
        {
            Customer = order.CustomerName,
            Notes = order.Note?.Text ?? string.Empty,
            Total = order.Total,
            Lines = order
                .Lines.OrderBy(static line => line.SortOrder)
                .Select(static line => new PlaygroundOrderLineDto
                {
                    Id = line.Id,
                    Product = line.Product,
                    Qty = line.Qty,
                })
                .ToList(),
        };

    internal static string Try(Func<string> render)
    {
        try
        {
            return render();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
