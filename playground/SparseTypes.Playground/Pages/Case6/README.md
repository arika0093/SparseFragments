Case 6 edits the catalog through separate Overview, Commercial, Operations, Workflow, and work-master child sessions. A `SparseSessionScope` retains child contexts by typed path and owns their histories. Each section uses `SparseEdit` and `SparseField` for scalar edits.

The **Enable section drafts** checkbox starts on. With it enabled, Save and Discard manage each scope's draft, and Root Save asks users to save or discard pending sections before opening the review. Turn it off to accept each edit into the catalog session as it occurs; section Save and Discard controls are hidden. The mode cannot be changed while section drafts are pending.

Workflow steps and work masters use separate keyed paths. Work-master tabs follow workflow steps accepted into the catalog session, so an unaccepted workflow draft does not add or remove those tabs. The workflow keeps its sortable template palette, clone insertion, ordering checks, and typed collection edits. Tier and discount edits use nested child sessions and keyed collection editors, with one `SparseCollectionState` snapshot shared by each row list.

The Codes tab shows the model declarations and the scope, field, history, and acceptance APIs used by the running example. The sample does not use `EditSessionGroup` or Fork.
