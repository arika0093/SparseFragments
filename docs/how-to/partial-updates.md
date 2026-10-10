# Partial updates

This guide serves one endpoint that accepts two kinds of input: a
baseline-free patch command from a caller that never saw the current state,
and a baseline-aware change set from an editor that did. It assumes the
[first sparse edit](../tutorial/first-sparse-edit.md) flow already reads
familiar. The wire envelope and reconciliation rules stay in their
references: [ChangePayload](../change-payload.md) for the JSON layout and
[ChangeSet rebase](../rebase.md) for the server-side save, including the
row-version guard. This page chooses the input and shows where information
is lost on purpose.

## Endpoint model

The profile carries a display name and a phone number. The phone number
redacts its before-state on the wire, which matters in the last section.

<!-- sample: partial-models -->
```csharp
using System.Text.Json;
using SparseFragments;

[SparseFragmentModel]
public partial class PartialProfile
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Phone { get; set; }
}
```
<!-- /sample -->

## Accept a Patch command with no preconditions

A caller that never read the row sends what it wants changed. The patch
carries no before-state, so the endpoint applies it without a conflict
check. Use this shape for callers that own their target outright, such as an
admin command or a webhook that sets one field.

<!-- sample: partial-patch -->
```csharp
var stored = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var storedState = PartialProfile.Fragment.From(stored);

// The caller never saw a baseline: a Patch carries no preconditions.
var command = new PartialProfile.Patch { DisplayName = "Ada" };
var commanded = command.ApplyTo(stored);
// commanded.DisplayName == "Ada"
// commanded.Phone == "old-phone"
```
<!-- /sample -->

## Accept a ChangeSet from an editor that saw the baseline

An editor that read the row sends the transition it observed. The change
set keeps the before-state, so the endpoint can rebase it onto the current
row and report a conflict instead of overwriting a concurrent edit. The
rebase itself runs on the server as shown in
[ChangeSet rebase](../rebase.md#disconnected-editing).

<!-- sample: partial-changeset -->
```csharp
var editorBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var editorEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };

// The editor saw the baseline, so the transition keeps its before-state.
var editorChanges = editorBase.CreateChangeSet(editorEdited);
// editorChanges.DisplayName.IsChanged == true
// editorChanges.DisplayName.Before.Value == "Ann"
// editorChanges.DisplayName.After.Value == "Ada"
// editorChanges.Phone.IsChanged == false
```
<!-- /sample -->

## Convert both inputs to ChangePayload

Both inputs cross the process boundary through the same envelope. A change
set enters it with `ToPayload()`; a patch enters it with
`ChangePayload.FromPatch`, which redacts every before-state by construction.

`ToPatch()` is the information-loss boundary between them. It discards the
before-state and returns the equivalent desired operations. The projection
replays the same after-state from the same baseline, but it can no longer
be inverted, composed, or rebased. There is no silent path back.

<!-- sample: partial-payload -->
```csharp
var payloadBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var payloadEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };
var transition = payloadBase.CreateChangeSet(payloadEdited);

// Both inputs travel through the same envelope type.
var transitionPayload = transition.ToPayload();
var commandPayload = PartialProfile.ChangePayload.FromPatch(
    new PartialProfile.Patch { DisplayName = "Ada" }
);

// ToPatch is the information-loss boundary: the before-state is gone,
// so the projection can replay but no longer rebase or report conflicts.
var replay = transition.ToPatch();
var replayed = replay.ApplyTo(payloadBase);
// replayed.DisplayName == "Ada"
```
<!-- /sample -->

## Audit and undo only complete change sets

Undo replays the before-state, so only the complete change set inverts. A
projected patch has nothing to walk back to. Keep the change set where the
audit trail or the undo stack needs it, and project to a patch at the last
moment before a blind apply.

<!-- sample: partial-audit -->
```csharp
var auditBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var auditEdited = new PartialProfile { DisplayName = "Ada", Phone = "old-phone" };
var auditChanges = auditBase.CreateChangeSet(auditEdited);

// Undo needs the before-state, so only the complete ChangeSet inverts.
var undo = auditChanges.Invert();
var restored = undo.ToPatch().ApplyTo(auditEdited);
// restored.DisplayName == "Ann"
```
<!-- /sample -->

## Treat a redacted rotation as a command, not history

The phone rotation redacts its before-state on the wire: the payload carries
the new number and never the old one. The redacted payload still projects
to a baseline-free command that applies cleanly. It cannot become a
baseline-aware change set again, because the before-state needed for
reversible history is gone by design. Endpoint code that needs the old value
for audit must capture it before redaction, not recover it after.

<!-- sample: partial-redacted -->
```csharp
var secretBase = new PartialProfile { DisplayName = "Ann", Phone = "old-phone" };
var secretEdited = new PartialProfile { DisplayName = "Ann", Phone = "new-phone" };
var rotation = secretBase.CreateChangeSet(secretEdited);

var wire = JsonSerializer.Serialize(rotation.ToPayload());
// wire carries "new-phone" but never "old-phone"
var incoming = JsonSerializer.Deserialize<PartialProfile.ChangePayload>(wire)!;

// The redacted payload still projects to a baseline-free command.
var replayed = incoming.ToPatch().ApplyTo(secretBase);
// replayed.Phone == "new-phone"

// It cannot become a baseline-aware ChangeSet again: the before-state
// is gone, so there is no reversible history to reconstruct.
var rejected = false;
try
{
    incoming.ToChangeSet();
}
catch (ArgumentException)
{
    rejected = true;
}
// rejected == true
```
<!-- /sample -->

The `JsonSerializer` lines need `using System.Text.Json;` at the top of the
file. The redaction attribute and the mixed-request endpoints that accept
both shapes in one call stay in the [ChangePayload reference](../change-payload.md).
