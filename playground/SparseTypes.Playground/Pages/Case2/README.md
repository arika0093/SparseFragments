# Case 2: Typed patch

This folder contains the patch page, patch editor, and sample code.

The editor distinguishes Keep, Set, Remove, and an explicit null for nullable
members. The code samples separate patch construction from the later steps
that create, serialize, and apply a ChangeSet.

The ChangePayload view exports a ChangeSet derived from the merged fragment.
Import deserializes the ordinary, non-redacted payload, reconstructs the
ChangeSet, and applies it to the same baseline. Before-redaction constraints
are covered in Case 4.
