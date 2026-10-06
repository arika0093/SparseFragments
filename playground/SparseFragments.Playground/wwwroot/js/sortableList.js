// Adapted from https://github.com/the-urlist/BlazorSortable (MIT).
// Blazor owns the row order: on drop the DOM move is reverted and .NET
// re-renders from the updated data.
let sortableCtorPromise = null;

function ensureSortable() {
    if (typeof Sortable === "function") {
        return Promise.resolve(Sortable);
    }
    // The index.html preload may have been skipped (e.g. cached page):
    // importing the UMD build executes it as a side effect and defines the global.
    sortableCtorPromise ??= import("./Sortable.min.js").then(() => {
        if (typeof Sortable === "function") {
            return Sortable;
        }
        throw new Error(
            "SortableJS global is missing after loading ./Sortable.min.js. " +
                "Check that the file is served (HTTP 200, text/javascript)."
        );
    });
    return sortableCtorPromise;
}

export async function init(
    id,
    group,
    pull,
    put,
    sort,
    handle,
    filter,
    ghostClass,
    chosenClass,
    component,
    forceFallback
) {
    const SortableCtor = await ensureSortable();
    var sortable = new SortableCtor(document.getElementById(id), {
        animation: 200,
        ghostClass: ghostClass || "sortable-ghost",
        chosenClass: chosenClass || "sortable-chosen",
        group: {
            name: group,
            pull: pull || true,
            put: put,
        },
        filter: filter || undefined,
        sort: sort,
        forceFallback: forceFallback,
        handle: handle || undefined,
        onUpdate: (event) => {
            // Revert the DOM to match the .NET state
            event.item.remove();
            event.to.insertBefore(event.item, event.to.childNodes[event.oldIndex]);

            // Notify .NET to update its model and re-render
            component.invokeMethodAsync(
                "OnUpdateJS",
                event.oldDraggableIndex,
                event.newDraggableIndex
            );
        },
        onRemove: (event) => {
            if (event.pullMode === "clone") {
                // Remove the clone
                event.clone.remove();
            }

            event.item.remove();
            event.from.insertBefore(event.item, event.from.childNodes[event.oldIndex]);

            // Notify .NET to update its model and re-render
            component.invokeMethodAsync(
                "OnRemoveJS",
                event.oldDraggableIndex,
                event.newDraggableIndex
            );
        },
    });
}
