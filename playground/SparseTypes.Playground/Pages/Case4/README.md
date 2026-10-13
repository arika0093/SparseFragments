# Case 4: Edit session

This folder contains the edit-session page, task model, and sample code. `TaskListEditor` and `TaskRow` keep task-list editing separate from session history and submission.

## Collection editing through the typed facade

Adds, drag reorders, removals, row moves, and id renames go through `Session.EditCollection<PlaygroundTask, string>(PlaygroundSettings.SparsePath.Tasks)`. Each call runs inside one batch, so one user action raises one transition notification and one opt-in history entry. The drag handler keeps the `SortableList` guards (stale indices and no-ops stay silent) and maps the drop position to facade coordinates: the list addresses the end as `Count`, the facade addresses the last position as `Count - 1`. A rename is one keyed replace, which keeps the row position and reports a removal plus an addition in the change set.

## Headless inputs over session paths

Scalar, nested, and task-row inputs render through `SparseEdit Session` plus typed `SparseField` contexts over `Session.Paths`, with session-owned history enabled once via `EnableHistory`. The edit root supplies the `EditContext`, refresh, and history surface, so the page keeps no handwritten field bindings, no keyed binding rebuilds, and no `ChangeSet`/`EditContext` prop drilling: rows read highlights from their own field state (`IsChanged`) and share one parent-captured collection snapshot for added and reordered rows. The submit preview still diffs the live model against the retained baseline. None of these reach past the typed surface into raw descriptors.

Case 3 stays on direct list mutation on purpose. Its roster editor owns no session and reports changes through a manual callback, while the facade needs session descriptors plus the batch runner. Migrating it would mean adopting a session first, which is out of scope here.
