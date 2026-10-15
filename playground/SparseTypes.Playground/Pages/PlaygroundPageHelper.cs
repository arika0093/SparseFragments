using SparseTypes.Generated;

namespace SparseTypes.Playground.Pages;

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

    // Audit: Cases 3-5 each mapped their own per-model ChangeKind plus a
    // Missing-to-"—" format.
    internal static string ChangeOperation(SparseChangeKind kind) =>
        kind switch
        {
            SparseChangeKind.Changed => "Edit",
            SparseChangeKind.Added => "Add",
            SparseChangeKind.Removed => "Remove",
            SparseChangeKind.Order => "Order",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    internal static string ChangeOperationClass(string operation) =>
        $"pg-change-operation pg-change-{operation.ToLowerInvariant()}";
}
