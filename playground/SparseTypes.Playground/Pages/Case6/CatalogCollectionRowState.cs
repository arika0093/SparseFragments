using SparseTypes.Blazor;

namespace SparseTypes.Playground.Pages.Case6;

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
