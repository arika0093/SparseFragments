# Case 6: Enterprise master editing

This case demonstrates editing a large catalog model through separate Overview, Commercial, Operations, Workflow, and work-master screens. Each screen uses an `EditSession` scope with its own Undo/Redo history. Switching tabs preserves pending scope changes, which remain visible on their tabs. The first three sections include editable lists, and the price-tier dialog contains a second list of discount bands.

The workflow screen uses Fluent UI's grouped sortable lists to reorder steps and add clones from the 20-step palette. Work-master tabs follow the current workflow draft, with a keyed scope for each step.

The catalog header reports changes saved to the catalog session and provides Reset and Save. Local section drafts appear as green Edited badges on their tabs. Save opens a full-width review grouped by Overview, Commercial, Operations, Workflow, and work-master changes before accepting them. The Codes tab shows the DTO shapes, nested scopes, per-scope history, and keyed work-master paths used by the sample.
