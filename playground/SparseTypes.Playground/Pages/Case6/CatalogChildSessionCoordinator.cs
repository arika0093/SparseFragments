using SparseTypes;

namespace SparseTypes.Playground.Pages.Case6;

public sealed class CatalogChildSessionCoordinator
{
    private const string WorkMasterPrefix = "work-master:";
    public const string OverviewId = "overview";
    public const string CommercialId = "commercial";
    public const string OperationsId = "operations";
    public const string WorkflowId = "workflow";

    private readonly Dictionary<string, bool> _states = new(StringComparer.Ordinal);

    public event Action? Changed;

    public static string WorkMasterId(string stepId) => $"{WorkMasterPrefix}{stepId}";

    public bool HasChangesFor(string id) =>
        _states.TryGetValue(id, out var hasChanges) && hasChanges;

    public void SetHasChanges(string id, bool hasChanges)
    {
        if (_states.TryGetValue(id, out var current) && current == hasChanges)
        {
            return;
        }

        _states[id] = hasChanges;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_states.Count == 0)
        {
            return;
        }

        _states.Clear();
        Changed?.Invoke();
    }
}

internal static class CatalogCollectionRowState
{
    public static string ClassFor(
        SparseCollectionItemState state,
        bool detectReorder = false,
        bool hasLocalChanges = false
    ) =>
        state.IsAdded ? "pg-roster-row pg-added"
        : detectReorder && state.IsReordered ? "pg-roster-row pg-moved"
        : state.IsEdited || hasLocalChanges ? "pg-roster-row pg-changed"
        : "pg-roster-row";
}
