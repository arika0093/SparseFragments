using SparseFragments;
using SparseFragments.Playground.Models;

namespace SparseFragments.Playground.Pages;

/// <summary>Rendering helpers shared by the per-case playground pages.</summary>
internal static class PlaygroundPageHelper
{
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

    internal static string ChangeOperation(PlaygroundRoster.ChangeSet.ChangeKind kind) =>
        kind switch
        {
            PlaygroundRoster.ChangeSet.ChangeKind.Changed => "Edit",
            PlaygroundRoster.ChangeSet.ChangeKind.Added => "Add",
            PlaygroundRoster.ChangeSet.ChangeKind.Removed => "Remove",
            PlaygroundRoster.ChangeSet.ChangeKind.Order => "Order",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    internal static string ChangeOperationClass(string operation) =>
        $"pg-change-operation pg-change-{operation.ToLowerInvariant()}";

    internal static string FormatRosterValue(Optional<object?> value) =>
        value.IsPresent ? PlaygroundJson.FormatChangeValue(value.Value) : "—";
}
