using SparseTypes;
using SparseTypes.Blazor;

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

    public string? Error { get; private set; }

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

    public bool TryAcceptImmediately<TModel>(ISparseChildEditContext<TModel, SparseConflict> child)
        where TModel : class
    {
        if (child.TryAcceptChanges(out var conflicts))
        {
            SetError(null);
            return true;
        }

        SetError(
            conflicts is null
                ? "The section edit could not be applied to the catalog."
                : $"The section edit conflicts at {string.Join(", ", conflicts.Select(static conflict => conflict.PathText))}."
        );
        return false;
    }

    public void SetError(string? error)
    {
        if (string.Equals(Error, error, StringComparison.Ordinal))
        {
            return;
        }

        Error = error;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_states.Count == 0 && Error is null)
        {
            return;
        }

        _states.Clear();
        Error = null;
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
