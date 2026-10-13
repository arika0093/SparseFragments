# Case 5: EF Core mapping

This folder contains the EF Core mapping, page, and sample code. `OrderLineRow` handles line editing, and `SaveSnackbar` shows progress during the delayed save. The submit action advances the draft baseline; a simulated concurrent edit triggers a rebase and opens a conflict dialog.
