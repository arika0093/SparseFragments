# Partial updates

This guide shows how to accept two kinds of updates in a Web API: baseline-free patch commands and baseline-aware change sets.

## Endpoint model

<!-- sample: partial-models -->
```csharp
using System.Text.Json;
using SparseTypes;

[SparseModel]
public partial class PartialProfile
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Phone { get; set; }
}
```
<!-- /sample -->

## Apply a baseline-free Patch command

When callers send desired values without having seen the current state (e.g. webhook or admin update), use `Patch`. It applies directly without conflict checking:

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

## Apply a baseline-aware ChangeSet with conflict detection

When an editor has seen the current baseline, capture and send a `ChangeSet`. The server uses the before-state to detect concurrent collisions:

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

## Wire transport with ChangePayload

Both inputs cross process boundaries using the unified `ChangePayload` envelope:

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

## Audit and Undo

Only complete `ChangeSet` instances can be inverted. Projecting to a `Patch` discards before-state:

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

## Redacted members

Members marked with `[SparseRedactBefore]` omit before-values on the wire. They apply cleanly as commands, but cannot be inverted or converted back to a reversible `ChangeSet`:

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

For serialization format details, see [ChangePayload reference](../reference/change-payload.md). For server rebase rules, see [ChangeSet rebase](../explanation/rebase.md).
