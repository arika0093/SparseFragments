# Case 5: EF Core mapping

This folder contains the EF Core mapping, page, and sample code. `OrderLineRow` handles line editing, and `SaveSnackbar` shows progress during the delayed save. The submit action captures an immutable ChangeSet through the generated EditSession; the baseline advances when the save is acknowledged, and a simulated concurrent edit triggers a rebase and opens a conflict dialog.
