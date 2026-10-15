using System.Text.Json;
using SparseTypes.Playground.Components;
using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case4;

/// <summary>Submit confirmation rows derived from a session ChangeSet snapshot.</summary>
/// <remarks>Presentation only: grouping, unquoted task keys, and operation ordering are applied after computing the semantic diff with the library API. The snapshot freezes the dialog while further edits continue; only the serialized baseline pane and the reserved baseline task IDs are retained separately.</remarks>
public static class SubmitPreview
{
    private const string MissingText = "—";

    /// <summary>Builds the grouped submit preview rows for one ChangeSet snapshot.</summary>
    /// <param name="changes">The snapshot captured when submission was requested.</param>
    /// <returns>Rows ordered by operation: adds, removes, edits, then the task order.</returns>
    public static IReadOnlyList<ChangePreviewRow> Build(PlaygroundSettings.ChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var rows = new List<(string Target, string Operation, string Before, string After)>();
        AddScalar(
            rows,
            nameof(PlaygroundSettings.Enabled),
            changes.Enabled.IsChanged,
            changes.Enabled.Before,
            changes.Enabled.After
        );
        AddScalar(
            rows,
            nameof(PlaygroundSettings.RetryCount),
            changes.RetryCount.IsChanged,
            changes.RetryCount.Before,
            changes.RetryCount.After
        );
        AddScalar(
            rows,
            nameof(PlaygroundSettings.Label),
            changes.Label.IsChanged,
            changes.Label.Before,
            changes.Label.After
        );
        AddPassword(rows, changes);
        AddScalar(
            rows,
            $"{nameof(PlaygroundSettings.Nested)}.{nameof(PlaygroundNested.Host)}",
            changes.Nested.Host.IsChanged,
            changes.Nested.Host.Before,
            changes.Nested.Host.After
        );
        AddScalar(
            rows,
            $"{nameof(PlaygroundSettings.Nested)}.{nameof(PlaygroundNested.Port)}",
            changes.Nested.Port.IsChanged,
            changes.Nested.Port.Before,
            changes.Nested.Port.After
        );
        AddCollection(
            rows,
            nameof(PlaygroundSettings.Plugins),
            changes.Plugins.IsChanged,
            changes.Plugins.Before,
            changes.Plugins.After
        );
        AddCollection(
            rows,
            nameof(PlaygroundSettings.Tags),
            changes.Tags.IsChanged,
            changes.Tags.Before,
            changes.Tags.After
        );
        AddTasks(rows, changes);

        // Stable sort keeps draft order within adds/edits and baseline order
        // within removes.
        return rows.OrderBy(static row => OperationOrder(row.Operation))
            .Select(static row => new ChangePreviewRow
            {
                Target = row.Target,
                Operation = row.Operation,
                Before = row.Before,
                After = row.After,
            })
            .ToList();
    }

    private static void AddScalar<T>(
        List<(string Target, string Operation, string Before, string After)> rows,
        string target,
        bool changed,
        Optional<T> before,
        Optional<T> after
    )
    {
        // A nulled nested model leaves leaf endpoints missing on one side;
        // those render as missing rather than JSON null, unlike the previous
        // handwritten baseline comparison.
        if (!changed || (!before.IsPresent && !after.IsPresent))
        {
            return;
        }

        rows.Add((target, "Edit", FormatScalar(before), FormatScalar(after)));
    }

    private static void AddPassword(
        List<(string Target, string Operation, string Before, string After)> rows,
        PlaygroundSettings.ChangeSet changes
    )
    {
        if (!changes.Password.IsChanged)
        {
            return;
        }

        // The typed endpoints carry the real value locally; only the masked
        // marker reaches the dialog.
        rows.Add(
            (
                nameof(PlaygroundSettings.Password),
                "Edit",
                PlaygroundJson.MaskPasswordSummaryValue(
                    nameof(PlaygroundSettings.Password),
                    FormatScalar(changes.Password.Before)
                ),
                PlaygroundJson.MaskPasswordSummaryValue(
                    nameof(PlaygroundSettings.Password),
                    FormatScalar(changes.Password.After)
                )
            )
        );
    }

    private static void AddCollection<T>(
        List<(string Target, string Operation, string Before, string After)> rows,
        string target,
        bool changed,
        Optional<T> before,
        Optional<T> after
    )
    {
        if (!changed)
        {
            return;
        }

        rows.Add((target, "Edit", FormatCollection(before), FormatCollection(after)));
    }

    private static void AddTasks(
        List<(string Target, string Operation, string Before, string After)> rows,
        PlaygroundSettings.ChangeSet changes
    )
    {
        var tasks = changes.Tasks;
        var added = tasks.Added.ToDictionary(static task => task.Id, StringComparer.Ordinal);
        var removed = tasks.Removed.ToDictionary(static task => task.Id, StringComparer.Ordinal);

        // Draft order for adds, baseline order for removes, and draft order
        // for edits: keys come from the typed orders, never from parsing
        // formatted path strings.
        foreach (var key in tasks.AfterOrder)
        {
            if (added.TryGetValue(key, out var task))
            {
                rows.Add(($"Tasks[{key}]", "Add", MissingText, FormatTask(task)));
            }
        }

        foreach (var key in tasks.BeforeOrder)
        {
            if (removed.TryGetValue(key, out var task))
            {
                rows.Add(($"Tasks[{key}]", "Remove", FormatTask(task), MissingText));
            }
        }

        foreach (var key in tasks.AfterOrder)
        {
            if (!tasks.Edited.TryGetValue(key, out var edit))
            {
                continue;
            }

            if (edit.Title.IsChanged)
            {
                rows.Add(
                    (
                        $"Tasks[{key}].Title",
                        "Edit",
                        FormatScalar(edit.Title.Before),
                        FormatScalar(edit.Title.After)
                    )
                );
            }

            if (edit.Points.IsChanged)
            {
                rows.Add(
                    (
                        $"Tasks[{key}].Points",
                        "Edit",
                        FormatScalar(edit.Points.Before),
                        FormatScalar(edit.Points.After)
                    )
                );
            }
        }

        if (tasks.OrderChanged)
        {
            rows.Add(
                ("Tasks", "Order", FormatOrder(tasks.BeforeOrder), FormatOrder(tasks.AfterOrder))
            );
        }
    }

    private static int OperationOrder(string operation) =>
        operation switch
        {
            "Add" => 0,
            "Remove" => 1,
            "Edit" => 2,
            "Order" => 3,
            _ => 4,
        };

    private static string FormatScalar<T>(Optional<T> endpoint) =>
        endpoint.IsPresent ? JsonSerializer.Serialize(endpoint.Value) : MissingText;

    private static string FormatCollection<T>(Optional<T> endpoint) =>
        endpoint.IsPresent ? PlaygroundJson.FormatChangeValue(endpoint.Value) : MissingText;

    private static string FormatTask(PlaygroundTask task) =>
        $"{{ Title = {JsonSerializer.Serialize(task.Title)}, Points = {task.Points} }}";

    private static string FormatOrder(IEnumerable<string> ids) => $"[{string.Join(", ", ids)}]";
}
