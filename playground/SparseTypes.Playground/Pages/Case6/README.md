Case 6 edits the catalog through separate Overview, Commercial, Operations, Workflow, and work-master child sessions. A `SparseSessionScope` retains child contexts by typed path and owns their histories. Each section uses `SparseEdit` and `SparseField` for scalar edits; rendering fields does not accept a child draft.

Section Save calls the child context's `TryAcceptChanges`. Discard, Undo, and Redo stay local to that context. Pending section drafts keep their tab badges, while the catalog header and review show changes accepted into the root session. Root Save asks users to save or discard pending sections before opening the review.

Workflow steps and work masters use separate keyed paths. Work-master tabs follow workflow steps accepted into the catalog session, so an unaccepted workflow draft does not add or remove those tabs. The workflow keeps its sortable template palette, clone insertion, ordering checks, and typed collection edits. Tier and discount edits use nested child sessions and keyed collection editors, with one `SparseCollectionState` snapshot shared by each row list.

The Codes tab shows the model declarations and the scope, field, history, and acceptance APIs used by the running example. The sample does not use `EditSessionGroup` or Fork.
