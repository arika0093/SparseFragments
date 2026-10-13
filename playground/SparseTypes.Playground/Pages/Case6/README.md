# Case 6: Session coordination

This folder contains the session-coordination page. `GroupDemo` edits two independent typed `SparseEdit` forms and coordinates them through Core `EditSessionGroup.Prepare/PrepareAsync` with per-section validation, snapshot display, and acknowledgement. `ScopeDemo` edits a nested one-shot `OpenScope` draft with explicit `TryCommit`, `Cancel`, and disposal. `ForkDemo` branches with `Fork`, previews `TryMergeFrom`, and adopts the merged session explicitly.

There is no `SparseEditGroup` component here on purpose: the group owns section identity and revisions in Core, while each form stays an ordinary `SparseEdit` root. Acknowledgements are per-section and never atomic across sessions.
