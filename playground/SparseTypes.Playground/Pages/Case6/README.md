# Case 6: Enterprise master editing

This case demonstrates editing a large catalog model through separate Overview, Commercial, Operations, Workflow, and work-master screens. Each section edits its model through a typed child `EditSession` with an independent Undo/Redo history. Workflow steps and work masters are separate keyed collections, so their draft sessions do not share an object path. Switching tabs preserves pending child-session changes.

The workflow screen uses Fluent UI's grouped sortable lists to reorder steps and add clones from the 20-step palette. Work-master tabs follow the workflow saved to the catalog session. Changes in the Workflow draft do not alter those tabs until the section draft is saved.

The catalog header reports changes accepted into the root session and provides Reset and Save. Pending child-session edits appear as Edited badges on their tabs, but do not enable the root actions until a section draft is saved. If any section draft remains edited, the root Save asks the user to save or discard it first. Afterward, Save opens a full-width review of the changes already accepted into the root session. The Codes tab shows the model shapes, child sessions, per-session history, and keyed work-master paths used by the sample.
