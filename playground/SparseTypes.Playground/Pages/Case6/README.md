# Case 6: Enterprise master editing

This case demonstrates editing a large catalog model through separate Overview, Commercial, Operations, Workflow, and work-master screens. Each section edits its model through a typed child `EditSession` with an independent Undo/Redo history. Work-master sessions are nested under the workflow session, so accepting or discarding the workflow also handles its work-master edits. Switching tabs preserves pending child-session changes.

The workflow screen uses Fluent UI's grouped sortable lists to reorder steps and add clones from the 20-step palette. Work-master tabs follow the current workflow draft, with a keyed child session for each step.

The catalog header reports changes in the root session and provides Reset and Save. Pending child-session edits appear as Edited badges on their tabs. Save accepts pending child sessions into the root session, then opens a full-width review grouped by Overview, Commercial, Operations, Workflow, and work-master changes. The Codes tab shows the model shapes, nested child sessions, per-session history, and keyed work-master paths used by the sample.
