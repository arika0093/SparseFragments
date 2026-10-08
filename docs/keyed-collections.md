# Keyed Collections

This page explains element identity and the per-key operations built on it.

SparseFragments can patch a structural list element by element only when each element has a stable key. Array positions are not stable identity: inserting an item at the front changes every later index even though those existing items are still the same logical objects.

<!-- sample: keyed-first-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class Fleet
{
    public List<Server> Servers { get; set; } = new();
}

public partial class Server
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}
```
<!-- /sample -->

<!-- sample: keyed-first -->
```csharp
var before = new Fleet
{
    Servers = new()
    {
        new Server { Id = "a", Host = "old" },
    },
};
var after = new Fleet
{
    Servers = new()
    {
        new Server { Id = "a", Host = "new" },
        new Server { Id = "b" },
    },
};

var changes = before.CreateChangeSet(after); // add/remove/edit by key
if (!changes.TryApplyTo(before, out var applied))
{
    throw new InvalidOperationException("The keyed changes conflict.");
}

// applied.Servers.Count == 2
// applied.Servers.Single(s => s.Id == "a").Host == "new"
```
<!-- /sample -->

## Atomic vs Keyed Collections

This section is a reference. It defines which patch semantics each collection kind uses.

| Collection kind | Examples | Patch semantics |
| --- | --- | --- |
| Scalar/atomic sequence | `List<string>`, `int[]` | Whole value: a patch sets or removes the entire collection |
| Dictionary/map | `Dictionary<string, int>` | Keyed by `TKey` inherently; entries patch by key unless explicitly configured as `Replace` |
| Structural sequence **with** a key | `List<Server>` where `Server` declares `[SparseKey]` | Keyed: add/remove/edit by element, reorder by key order |
| Structural sequence **without** a key | `List<Server>` with no key declared | Generator error (`SPF011`): declare a key, or explicitly opt into `Replace`, `Append`, `SetUnion`, or a custom strategy on the member |

An explicit `[SparseMerge(MergeMode.Replace)]` always selects whole-value semantics, even for a keyed list or a dictionary. This changes the generated Patch/ChangeSet shape and serialized wire format from per-key operations to a replacement value. The implicit `Default` still uses keyed operations when key metadata is available; key declarations continue to be validated even when explicit replacement is selected.

A structural element is an element SparseFragments can patch through its generated member-level Fragment and Patch API. This includes explicit fragment models and eligible reachable `partial` types. A sequence of scalars is never structural, no matter what merge mode is configured.

When no key is available and per-element patching is not needed, select whole-collection semantics explicitly:

```csharp
[SparseFragmentModel]
public partial class Settings
{
    [SparseMerge(MergeMode.Append)]
    public List<AuditEntry> History { get; set; } = new();
}
```

## Declaring Identity

This section is a how-to. It shows the three key mechanisms.

Exactly one key-definition mechanism may apply to a structural type. There is no precedence between them: combining two mechanisms is a generator error (`SPF012`). Key equality uses the normal equality semantics of the key type (`EqualityComparer<T>.Default`).

### Single-property key

Mark one property with parameterless `[SparseKey]`:

```csharp
public partial class Server
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}

[SparseFragmentModel]
public partial class Inventory
{
    public List<Server> Servers { get; set; } = new();
}
```

The property type is the key type. Computed/read-only properties are valid keys as long as they are publicly readable instance properties:

```csharp
public readonly record struct ServerKey(string TenantId, int Id);

public partial class Server
{
    public string TenantId { get; set; } = string.Empty;
    public int Id { get; set; }

    [SparseKey]
    public ServerKey Key => new(TenantId, Id);
}
```

### Composite key

Declare an ordered composite on the type with explicit, order-significant components:

```csharp
[SparseKey(nameof(TenantId), nameof(Id))]
public partial class Server
{
    public string TenantId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
}
```

The generated composite key is a strongly typed tuple of the component values in declaration order, compared component-wise. Component order is significant: `(tenant, id)` and `(id, tenant)` are different key shapes. Marking more than one property with `[SparseKey]` is not a composite key (`SPF013`); use the type-level form instead.

### Computed keys with `ISparseKeyed<TKey>`

When identity cannot be expressed as a key property or an ordered composite (for example a normalized or case-folded key), implement `ISparseKeyed<TKey>`:

```csharp
public partial class Server : ISparseKeyed<ServerKey>
{
    public string Tenant { get; set; } = string.Empty;
    public int Id { get; set; }

    public ServerKey SparseKey => new(Tenant.ToUpperInvariant(), Id);
}
```

Implement exactly one `ISparseKeyed<TKey>` with a publicly readable instance `SparseKey` getter (explicit interface implementations are not reachable by generated code, `SPF020`).

### Key constraints

Keys must be stable and comparable. Each rule is enforced at generation time:

* non-nullable (`string?`, `int?`, and similar are rejected, `SPF018`);
* not collection-shaped (arrays, `List<T>`, dictionaries, and sets are rejected, `SPF019`);
* publicly readable instance properties. Static, indexer, or non-publicly-readable properties cannot serve as identity (`SPF017`);
* value-object keys (records, record structs, enums, strings, GUIDs, and ordinary scalars) are valid when they provide stable equality.

## Add, Remove, Edit, and Reorder

This section is a how-to. It shows how per-key operations derive from before and after states.

`before.CreateChangeSet(after)` derives per-element operations from the before and after key sets; `ToPatch().ApplyTo` replays them to a model. The final key order, not positional moves, determines the resulting order. Replaying reproduces the after-state exactly (a follow-up `applied.CreateChangeSet(after).IsEmpty` holds).

The same `Fleet` / `Server` model shows each operation with ordinary values:

```csharp
var before = new Fleet
{
    Servers = new() { new Server { Id = "a", Host = "A" }, new Server { Id = "b", Host = "B" } },
};
var after = new Fleet
{
    Servers = new() { new Server { Id = "b", Host = "B2" }, new Server { Id = "c", Host = "C" } },
};

var changes = before.CreateChangeSet(after);
var applied = changes.ToPatch().ApplyTo(before);
// applied holds keys ["b", "c"]; "b" was edited in place, "a" removed, "c" added.
```

Concretely:

* `Add.` An element whose key exists only in `after` is added. Its full fragment state is carried by the patch.
* `Remove.` An element whose key exists only in `before` is removed by key.
* `Edit.` An element present in both is patched through its generated nested patch, so only the changed members travel. Editing nested members, nested keyed collections, and whole-value members all uses the same generated Patch behavior as the rest of the model.
* `Replace the whole collection.` Assigning a fresh collection to the member (a `Set` on the collection member itself) replaces the container wholesale rather than diffing elements.
* `Reorder.` The resulting order is the final key order. Reversing `["a", "b"]` to `["b", "a"]` is a real (non-empty) patch whose replay reproduces the new order; there is no separate move identity.

Keyed collections compose recursively: a keyed element type may itself hold keyed collections (for example teams holding keyed members), and each level diffs by its own keys. Keyed members rebase element-wise where the keys line up; divergent per-key edits surface as structured conflicts (see [ChangeSet rebase](rebase.md)).

## Database-assigned keys

This section is a how-to. It shows how to handle client-created elements before the database assigns identity.

For client-created elements whose database identity is assigned later, opt in to one
property-level sentinel:

<!-- sample: keyed-unassigned-model -->
```csharp
[SparseFragmentModel]
public partial class PendingFleet
{
    public List<PendingServer> Servers { get; set; } = new();
}

public partial class PendingServer
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Host { get; set; } = string.Empty;
}
```
<!-- /sample -->

An element with `Id == 0` is always a new addition. Any number of such elements may
appear in the after-state, and their positions are preserved. The sentinel is never
implicit: without `Unassigned`, keys remain unique exactly as before.

Composite
`[SparseKey(...)]` keys and `ISparseKeyed<TKey>` are not supported with a sentinel
(`SPF025`); a sentinel incompatible with the marked property's key type is an error
(`SPF024`).

An unassigned element cannot appear in a before/baseline collection for `Between`,
`Apply`, `Compose`, or `Rebase`; the operation throws `InvalidOperationException`.
Because it has no stable identity, an unassigned element cannot be edited or removed
by key. Assign a real key first, then treat the persisted element as part of the
baseline.

The keyed ChangeSet payload carries unassigned additions as ordinary keyed
transitions. When they need explicit ordering, the `"afterOrder"` array may contain
repeated sentinel key values. Each occurrence identifies the next sentinel-valued
item in transition order (ordinary keys continue to identify their elements
directly). Thus `[0, 7, 0]` positions two unassigned additions around key `7`;
no tagged key format is introduced.

<!-- sample: keyed-unassigned-flow -->
```csharp
var before = new PendingFleet { Servers = new() { new PendingServer { Id = 4 } } };
var after = new PendingFleet
{
    Servers = new()
    {
        new PendingServer { Id = 0, Host = "client-1" },
        new PendingServer { Id = 4, Host = "saved" },
        new PendingServer { Id = 0, Host = "client-2" },
    },
};
var changes = before.CreateChangeSet(after);
// Send changes.ToPayload() to the server; the server inserts the rows,
// assigns database IDs, and may normalize or reorder them.
// Do not acknowledge the unassigned transition with AcceptChanges.
// Replace with the authoritative state and start a fresh session:
var persisted = new PendingFleet
{
    Servers = new()
    {
        new PendingServer { Id = 11, Host = "client-1" },
        new PendingServer { Id = 4, Host = "saved" },
        new PendingServer { Id = 12, Host = "client-2" },
    },
};
var session = persisted.CreateEditSession();
// session.HasChanges == false
```
<!-- /sample -->

After the server inserts the rows and assigns their IDs, replace the client model
with the authoritative returned state (or refetch it) and create a fresh edit
session from that state. Recreate the `EditContext` from the fresh session so
field tracking restarts from the assigned IDs. This ensures subsequent keyed
edits use the assigned IDs rather than the sentinel.

`AcceptChanges` rejects a transition that would retain unassigned sentinels, so the unassigned change set
itself is never acknowledged. No GUID auto-correlation or key remapping is
provided. Where the authoritative refresh is not implemented, disable editing
while a save is in flight.

## Observe Typed Collection Transitions

This section is a reference. It defines the per-key projections.

The same `Fleet` and `Server` model observes the transition through typed projections, without reflection, property descriptors, or `object?` casts:

<!-- sample: keyed-typed -->
```csharp
var before = new Fleet
{
    Servers = new()
    {
        new Server { Id = "a", Host = "A" },
        new Server { Id = "b", Host = "B" },
    },
};
var after = new Fleet
{
    Servers = new()
    {
        new Server { Id = "b", Host = "B2" },
        new Server { Id = "c", Host = "C" },
    },
};

var changes = before.CreateChangeSet(after);
var servers = changes.Servers;
// servers.Added.Single().Id == "c"
// servers.Removed.Single().Id == "a"
// servers.Edited["b"].Host.After.Value == "B2"
// servers.BeforeOrder.SequenceEqual(["a", "b"])
// servers.AfterOrder.SequenceEqual(["b", "c"])
// servers.OrderChanged == true
foreach (var item in servers)
{
    if (item.IsEdited)
    {
        Console.WriteLine(item.Edit.Host.IsChanged);
    }
}
var edited = servers.GetChange("b");
// edited.IsEdited == true
// edited.Edit.Host.After.Value == "B2"
```
<!-- /sample -->

Concretely:

* `Added` and `Removed` carry the full element values for keys that exist only in `after` or only in `before`.
* `Edited` maps each surviving changed key to its typed nested element transition, so `servers.Edited["b"].Host` is the same `IsChanged`, `Before`, and `After` shape as any other member transition.
* `BeforeOrder` and `AfterOrder` carry the full key order on each side; `OrderChanged` reports whether the two orders differ. A pure reorder (same keys, different order) is a real non-empty transition whose replay reproduces the final key order. Patches store final order, never a synthetic move operation.
* `Enumeration` visits one item per changed key (added, removed, edited, or reordered). Each item reports `Key`, `IsAdded`, `IsRemoved`, `IsEdited`, `IsReordered`, `Before` and `After` element snapshots, absolute `BeforeIndex` and `AfterIndex`, and the nested `Edit` transition. Enumerating a transition with no changes visits nothing.
* `GetChange(key)` looks up a single key's item: it returns the typed item for added, removed, edited, or reordered keys and an empty item (`IsEmpty == true`, never `null`) for unchanged or unknown keys.

A collection transition object may be enumerable while the root ChangeSet is not: collection items share one `TKey` and `TElement` type, whereas root model members are heterogeneous. Like the scalar projections, `Added`, `Removed`, `Edited`, enumeration, and the order views are API projections over the before and after state, not duplicate wire fields (see [Fragments and patches](fragments-and-patches.md)).

## Duplicate Keys and Key Changes

This section is a reference. It defines invalid identity states.

* `Duplicate keys are invalid.` A collection state containing the same key twice has no well-defined element identity; deriving a patch from or onto such a state throws `InvalidOperationException`.
* `Changing an element's identity is remove-old plus add-new.` If an edit changes the key property itself (for example renaming `Id` from `"a"` to `"b"`), the result is the removal of `"a"` plus the addition of `"b"`, never a silent retargeting of the edit onto a different element. State that would require retargeting round-trips as remove plus add through `CreateChangeSet`, `ToPatch`, and `ApplyTo`.
