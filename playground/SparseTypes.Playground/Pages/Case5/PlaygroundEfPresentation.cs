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

    // Use the shared projection for ChangeSet previews; EF-state and conflict
    // rows keep separate mappings because they use different record types.
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
