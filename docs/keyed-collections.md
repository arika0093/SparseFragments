# Keyed Collections

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

The collection kind determines how patches apply to its elements:

| Collection kind | Examples | Patch semantics |
| --- | --- | --- |
| Scalar/atomic sequence | `List<string>`, `int[]` | Whole value: a patch sets or removes the entire collection |
| Dictionary/map | `Dictionary<string, int>` | Keyed by `TKey` inherently; entries patch by key unless explicitly configured as `Replace` |
| Structural sequence **with** a key | `List<Server>` where `Server` declares `[SparseKey]` | Keyed: add/remove/edit by element, reorder by key order |
| Structural sequence **without** a key | `List<Server>` with no key declared | Generator error (`SPF011`): declare a key, or explicitly opt into `Replace`, `Append`, `SetUnion`, or a custom strategy on the member |

An explicit `[SparseMerge(MergeMode.Replace)]` always selects whole-value semantics, even for a keyed list or a dictionary. This changes the generated Patch/ChangeSet shape and serialized wire format from per-key operations to a replacement value. The implicit `Default` still uses keyed operations when key metadata is available; key declarations continue to be validated even when explicit replacement is selected.

A structural element is an element SparseFragments can patch through its generated member-level Fragment and Patch API. This includes explicit fragment models and eligible reachable `partial` types. A sequence of scalars is never structural, no matter what merge mode is configured.

Choose keyed patching when clients edit, add, remove, or reorder individual items and the server must reconcile those edits against concurrent state: the change set carries per-key operations that rebase element-wise. Choose whole replacement (`Replace`, `Append`, `SetUnion`, or a custom strategy) when the collection is written as one value, such as an append-only history or a computed snapshot. Whole replacement gives up per-item tracking for that member: merges combine whole values and payloads carry one replacement instead of granular entries.

When no key is available and per-element patching is not needed, select whole-collection semantics explicitly:

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
[SparseFragmentModel]
public partial class Settings
{
    [SparseMerge(MergeMode.Append)]
    public List<AuditEntry> History { get; set; } = new();
}
```

## Declaring Identity

Mark exactly one publicly readable instance property per element type with parameterless `[SparseKey]`. That property's type is the key type, compared with `EqualityComparer<TKey>.Default`. This single property form is the only supported declaration: marking two properties is an error (`SPF013`), and constructor arguments on the property or any type-level use is an error (`SPF014`).

### Ordinary scalar keys

The `Server` model above shows the common case: a writable `Id` property carries identity while the remaining properties stay editable. A getter-only computed property qualifies as well, since generated code reads the key for identity and never constructs or overlays it:

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
public partial class Server
{
    public string TenantId { get; set; } = string.Empty;
    public int Id { get; set; }

    [SparseKey]
    public string Key => $"{TenantId}/{Id}";

    public string Host { get; set; } = string.Empty;
}
```

### Composite identity in one computed property

A composite identity is one computed property whose type holds every component. Tuples and value objects both work; comparison applies to the whole key value, so component order is significant:

<!-- sample: keyed-composite-model -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class KeyedOrder
{
    public List<KeyedOrderLine> Lines { get; set; } = new();
}

public partial class KeyedOrderLine
{
    public string TenantId { get; set; } = string.Empty;

    public int LineNumber { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public (string TenantId, int LineNumber) Key => (TenantId, LineNumber);
}

public readonly record struct KeyedSku
{
    public string Code { get; init; }
}

public partial class KeyedProduct
{
    public string RawCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public KeyedSku Key => new() { Code = RawCode.ToUpperInvariant() };
}

[SparseFragmentModel]
public partial class KeyedCatalog
{
    public List<KeyedProduct> Products { get; set; } = new();
}
```
<!-- /sample -->

`(TenantId, LineNumber)` and `(LineNumber, TenantId)` are different key shapes. A normalized value-object key such as `KeyedSku` fits the same API when the key type provides stable equality: case-folded, trimmed, or otherwise derived identity lives in the computed property while the raw business fields stay editable. A nullable key type (including a nullable tuple) uses `null` as its unassigned sentinel; see [Unassigned keys](#unassigned-keys) and [Temporary identity](#temporary-identity-for-pending-additions).

### Key constraints

Keys must be stable and reachable. Each rule is enforced at generation time:

* publicly readable instance properties. Static, indexer, or non-publicly-readable properties cannot serve as identity (`SPF017`). Getter-only computed properties qualify.
* not collection-shaped (arrays, `List<T>`, dictionaries, and sets are rejected, `SPF019`);
* exactly one mark per element type. A second `[SparseKey]` property is an error (`SPF013`); constructor arguments or type-level use is an error (`SPF014`).
* a key property cannot be covered by `[SparseIgnore]` (`SPF022`). Identity must stay visible to generated collection operations.
* an explicit `[SparseKey(Unassigned = ...)]` sentinel must be a constant convertible to the key property type (`SPF024`).
* nullable key types are allowed. `null` is the unassigned sentinel for that key, which marks elements whose permanent identity is not assigned yet.

## Add, Remove, Edit, and Reorder

`before.CreateChangeSet(after)` derives per-element operations from the before and after key sets; `ToPatch().ApplyTo` replays them to a model. The final key order, not positional moves, determines the resulting order. Replaying reproduces the after-state exactly (a follow-up `applied.CreateChangeSet(after).IsEmpty` holds).

The same `Fleet` / `Server` model shows each operation with ordinary values:

<!-- illustrative: excerpt; uses guide-local model names and does not compile as written -->
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

### Avoiding unintended order changes

Because SparseFragments tracks element order as part of collection state, reordering items in a collection produces a non-empty change set with `OrderChanged == true`, even when individual elements are unchanged.

When UI sorting should not be recorded as a persisted data change, use either of two strategies:

1. **Sort in the view layer:** Keep the underlying model collection in its storage order and sort only during presentation (for example via LINQ `.OrderBy(...)` or a UI collection view).
2. **Normalize before diffing:** When models hold an explicit sequence property (such as `Order` or `SortIndex`), sort the model collection by that property before diffing or creating a change set:

<!-- illustrative: excerpt; uses names from the surrounding prose and does not compile as written -->
```csharp
items.Sort((a, b) => a.Order.CompareTo(b.Order));
var changes = session.CreateChangeSet();
```

Keyed collections compose recursively: a keyed element type may itself hold keyed collections (for example teams holding keyed members), and each level diffs by its own keys. Keyed members rebase element-wise where the keys line up; divergent per-key edits surface as structured conflicts (see [ChangeSet rebase](rebase.md)).

## Unassigned keys

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
implicit: without `Unassigned`, keys remain unique exactly as before. A nullable key
type needs no explicit sentinel: `null` marks unassigned elements the same way.

Without `[SparseTemporaryKey]`, an unassigned element has no stable identity of its
own. An unassigned element cannot appear in a before/baseline collection for `Between`,
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

The refresh pattern is the same recommended save workflow as any other edit session: disable editing while the save is in flight, send the payload, then start a fresh session from the authoritative state (see [UI frameworks](ui-frameworks.md#recommended-save-workflow)). The payload itself travels like any other change set through the application's own transport (see [ChangeSet rebase](rebase.md)).

When pending additions must stay editable while unassigned, survive a session
`Fork`, and correlate with assigned keys after the save, opt in to
[Temporary identity](#temporary-identity-for-pending-additions) instead.

## Temporary identity for pending additions

A `[SparseTemporaryKey]` gives an unassigned element a stable Guid identity that
works before the database assigns its permanent key. The declaration below is a
complete DTO: an `int` permanent key with `0` as its unassigned sentinel, plus an
opt-in nullable Guid beside it.

<!-- sample: keyed-temporary-model -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class KeyedOrderBook
{
    public List<KeyedOrderLineDto> Lines { get; set; } = new();
}

public partial class KeyedOrderLineDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}
```
<!-- /sample -->

Declaration rules, all enforced at generation time:

* Mark exactly one property per keyed element type with parameterless
  `[SparseTemporaryKey]`. The property type must be `Guid?`, publicly readable
  and settable (`SPF031`).
* The element's `[SparseKey]` must carry unassigned semantics: either an
  explicit `Unassigned` sentinel or a nullable key type whose sentinel is
  `null`. A temporary key beside a fully assigned key is rejected (`SPF031`).
* The marked property must not declare an explicit initializer. Any `= ...`
  clause warns, including `= null`, `= default`, and `= Guid.NewGuid()`
  (`SPF032`). The check is syntactic: assign temporary values in constructors
  or object initializers instead.

New rows acquire their Guid at construction through an object initializer:

<!-- illustrative: excerpt; uses guide-local model names and does not compile as written -->
```csharp
var added = new KeyedOrderLineDto { Name = "new", TemporaryId = Guid.NewGuid() };
```

Nothing auto-generates Guids for GET DTOs or generated constructors. A normal GET
leaves `TemporaryId` null.

`null` and invalid Guids behave as follows. A `null` temporary value means the
element carries no temporary identity: the legacy unassigned rules apply to it.
`Guid.Empty` is invalid, a missing temporary Guid on an unassigned element of a
temporary-keyed collection is invalid, and two elements sharing one Guid in a
collection are invalid. Each case fails keyed diffing with
`InvalidOperationException` rather than guessing which element is which.

Identity precedence decides which value identifies an element. A valid assigned
permanent key always wins: the temporary value never overrides assigned-key
identity, equality, or diffing. The temporary Guid identifies an element only
while its permanent key is unassigned. A response element may therefore carry
both an assigned permanent key and its original temporary Guid. Keep the value
as stored: when the permanent key is assigned, a later GET returning
`TemporaryId = null` compares identically and produces no change, so the client
never needs to clear the property after a save.

While unassigned, pending additions behave like ordinary keyed elements. They are
separately editable, removable, and reorderable by temporary Guid; they may
serve as an edit baseline, so `AcceptChanges` advances over them and a session
forked after an addition starts clean. Typed transitions expose them through
`GetTemporaryChange(Guid)`, which returns an empty item for unknown Guids, and
typed paths address them through `.TemporaryKey(Guid)` beside `.Key(TKey)`:

<!-- sample: keyed-temporary-flow -->
```csharp
var book = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "saved" },
    },
};
var session = book.CreateEditSession();

var first = new KeyedOrderLineDto { Name = "first", TemporaryId = Guid.NewGuid() };
var second = new KeyedOrderLineDto { Name = "second", TemporaryId = Guid.NewGuid() };
book.Lines.Add(first);
book.Lines.Add(second);

var pending = session.CreateChangeSet();
// pending.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsAdded == true
// pending.Lines.GetTemporaryChange(second.TemporaryId!.Value).IsAdded == true

session.AcceptChanges(pending);

book.Lines.Single(item => item.TemporaryId == second.TemporaryId).Name = "second v2";
var edited = session.CreateChangeSet();
// edited.Lines.GetTemporaryChange(second.TemporaryId!.Value).IsEdited == true

var path = KeyedOrderBook.SparsePath.Lines.TemporaryKey(first.TemporaryId!.Value).Name;
// path addresses the pending row by its temporary Guid, never by position

var wire = JsonSerializer.Serialize(pending.ToPayload());
var restored = JsonSerializer
    .Deserialize<KeyedOrderBook.ChangePayload>(wire)!
    .ToChangeSet();
// restored.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsAdded == true
```
<!-- /sample -->

`.Key(TKey)` and `.TemporaryKey(Guid)` are different segment kinds even when the
permanent key type is `Guid`: the two paths never compare equal, and assigning a
permanent key never rewrites an existing path. The wire text of a temporary
segment reads `Lines[temp:"<guid>"].Name` and parses back to the same path.

Temporary identity travels with the element through Fragment snapshots and
cloning, Patch and ChangeSet algebra (`Compose`, `Invert`, `Rebase`), typed
transitions, and the versioned ChangePayload envelope, including JSON
round-trips. Ordering across several pending additions is exact: each addition
keeps its own Guid rather than sharing the sentinel plus a position.

`TemporaryId` carries identity metadata, not an editable business field.
Generated code retains it on snapshots, serializes it for unassigned additions
and keyed transitions, and never reports it as a scalar edit: on a transition
item whose permanent key is assigned, `TemporaryKey` reads null. Do not mark the
property `[SparseIgnore]`: ignoring it would discard identity information the
transitions require. Keep editable domain values (names, quantities, display
codes) on regular properties, and keep the surrogate permanent key immutable, so
renaming a business field never removes and re-adds the row (see
[Duplicate Keys and Key Changes](#duplicate-keys-and-key-changes)).

## Saving pending additions with Reconcile

`TryReconcile` acknowledges a submitted change set against the authoritative
persisted DTO while the user keeps editing. It correlates each submitted
unassigned element with its assigned persisted element through their shared
temporary Guid, writes the assigned keys into the live model, and adopts the
persisted model as the new baseline with post-submit local edits preserved:

<!-- sample: keyed-reconcile-flow -->
```csharp
var draft = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "saved" },
    },
};
var edit = draft.CreateEditSession();

var added = new KeyedOrderLineDto { Name = "new", TemporaryId = Guid.NewGuid() };
draft.Lines.Add(added);
var submitted = edit.CreateChangeSet();

// Editing continues while the save is in flight: rename the pending
// row and add one more row.
draft.Lines.Single(item => item.TemporaryId == added.TemporaryId).Name = "new v2";
var late = new KeyedOrderLineDto { Name = "late", TemporaryId = Guid.NewGuid() };
draft.Lines.Add(late);

// The server inserts the submitted row, assigns Id 11, and echoes the
// temporary Guid on the ordinary response DTO.
var persisted = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "SAVED" },
        new KeyedOrderLineDto
        {
            Id = 11,
            Name = "NEW",
            TemporaryId = added.TemporaryId,
        },
    },
};

if (!edit.TryReconcile(submitted, persisted, out var reconcileError))
{
    throw new InvalidOperationException(reconcileError);
}

// The post-submit rename replays onto the assigned row: local field
// values win while the server contributes the identity.
// draft.Lines.Single(item => item.TemporaryId == added.TemporaryId).Id == 11
// edit.HasChanges == true: the late row stays pending with its own Guid
```
<!-- /sample -->

The client call around it keeps the usual shape: capture `submitted`, send
`submitted.ToPayload()` through the application transport with `await
api.UpdateAsync(...)`, then reconcile with the returned DTO. Only one
unacknowledged submission should be reconciled at a time, and reconciling an
already-reconciled state succeeds without further changes.

On success the persisted model becomes the baseline, so later diffs use
assigned keys. Edits made after submission stay pending against that baseline:
the late row above remains an addition under its own Guid, an edit to a pending
row retargets onto the assigned key, and a row dropped locally while the save
was in flight records as a removal of the assigned row.

On failure `TryReconcile` returns `false` with an error string and mutates
neither the live model nor the retained baseline. The response fails closed:
each newly assigned persisted element must echo its original temporary Guid. A
response that omits a needed Guid, duplicates one Guid across two assigned
rows, or leaves a submitted element unassigned is rejected rather than matched
by position, name, or value. A stale or foreign submitted change set is
rejected the same way.

Pick the acknowledgement call by what the server returned:

| Server result | Session call | Effect |
| --- | --- | --- |
| Persisted with assigned keys or normalization, ordinary DTO returned | `TryReconcile(submitted, persisted)` | Assigned keys land in the live model; persisted state becomes the baseline; post-submit edits stay pending |
| Persisted exactly the submitted transition, no key or value changes | `AcceptChanges(submitted)` | The retained baseline advances by the transition; the live model is untouched |
| Current state is authoritative, keep editing from it | `Reload(serverState)` | Pending edits rebase onto the server state in place with structured conflicts on overlap |
| Persisted state returned and no post-submit edits must be kept | New session from the persisted state | Clean baseline on the authoritative model |

`Reload` and the manual `TryApplyTo` merge remain available for server states
that arrive outside a save, as described in [UI frameworks](ui-frameworks.md#reload-with-fresh-server-state).
`EditSession.Fork()` works with newly added identifiable elements: a fork taken
after an addition carries the temporary baseline, so each side keeps editing
pending rows independently. Fork mechanics stay as described in
[UI frameworks](ui-frameworks.md#branch-speculative-edits).

## Server mapping: DTOs to EF Core entities

The DTO and the EF Core entity are different types. The DTO declares the
temporary property; the entity never carries it, so no `[NotMapped]` attribute
is needed on the entity:

<!-- sample: keyed-server-models -->
```csharp
public sealed class KeyedLineEntity
{
    public int Id;

    public string Name = string.Empty;
}
```
<!-- /sample -->

The endpoint flow below uses a plain in-memory stand-in for the `DbContext`, so
the correlation stays visible without an EF Core dependency. Production code
replaces the list with tracked entities and `SaveChangesAsync`:

<!-- sample: keyed-server-mapping -->
```csharp
var requested = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "saved" },
        new KeyedOrderLineDto { Name = "new", TemporaryId = Guid.NewGuid() },
    },
};

// The server maps each DTO line onto a tracked entity. The entity
// carries no TemporaryId; the pair list holds the correlation.
var tracked = new List<(Guid TemporaryId, KeyedLineEntity Entity)>();
var entities = requested
    .Lines.Select(dto =>
    {
        var entity = new KeyedLineEntity { Id = dto.Id, Name = dto.Name };
        if (entity.Id == 0 && dto.TemporaryId.HasValue)
        {
            tracked.Add((dto.TemporaryId.Value, entity));
        }
        return entity;
    })
    .ToList();

// SaveChanges assigns the permanent keys.
var nextId = 11;
foreach (var entity in entities)
{
    if (entity.Id == 0)
    {
        entity.Id = nextId++;
    }
}

// The response maps entities back onto the ordinary DTO shape and
// restores each new row's temporary Guid by its assigned key. The
// lookup is keyed by assigned permanent key, so the server may
// re-query before building the response.
var savedById = tracked.ToDictionary(pair => pair.Entity.Id, pair => pair.TemporaryId);
var response = new KeyedOrderBook
{
    Lines = entities
        .Select(entity => new KeyedOrderLineDto
        {
            Id = entity.Id,
            Name = entity.Name,
            TemporaryId = savedById.TryGetValue(entity.Id, out var temp) ? temp : null,
        })
        .ToList(),
};
// response.Lines.Single(line => line.Id == 11).TemporaryId == requested.Lines[1].TemporaryId
```
<!-- /sample -->

Concretely, the endpoint validates the incoming change set, applies it to its
DTO copy, maps added DTO lines to new tracked entities, updated DTO lines onto
their tracked entities, and removed DTO lines to deletes. It records one
`(TemporaryId, entity reference)` pair per added line, saves, then maps the
authoritative entity graph back onto the ordinary response DTO and restores
each created row's `TemporaryId` using its assigned permanent key. When the
endpoint re-queries after `SaveChanges`, it retains the
`(TemporaryId, assigned permanent key)` pairs until the response is built. For
composite keys the pair carries the full assigned key, such as
`(TenantId, Number)`. A normal GET maps the same DTO shape with
`TemporaryId == null`. No special key-mapping response envelope and no
EF-specific SparseFragments API take part: the client correlates through the
ordinary persisted DTO.

Persistence policy stays application-owned. Validation, optimistic concurrency
(a `rowversion` or equivalent token, see [ChangeSet rebase](rebase.md#save-under-a-concurrency-token)),
authorization, database transactions, and request idempotency belong to the
endpoint. A temporary Guid correlates one submission with its response; it does
not make the request idempotent on retry.

## Observe Typed Collection Transitions

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

* `Duplicate keys are invalid.` A collection state containing the same key twice has no well-defined element identity; deriving a patch from or onto such a state throws `InvalidOperationException`. The same holds for temporary identity: two unassigned elements sharing one Guid, an unassigned element with `Guid.Empty`, and an unassigned element with a missing Guid all fail keyed diffing rather than merging silently.
* `Changing an element's identity is remove-old plus add-new.` If an edit changes the key property itself (for example renaming `Id` from `"a"` to `"b"`), the result is the removal of `"a"` plus the addition of `"b"`, never a silent retargeting of the edit onto a different element. State that would require retargeting round-trips as remove plus add through `CreateChangeSet`, `ToPatch`, and `ApplyTo`.
* `Natural keys vs stable identity.` When an editable domain attribute (such as a username, part code, or display name) serves as `[SparseKey]`, modifying it in a form causes the element to be removed and re-added rather than edited in place. To preserve row identity across edits, give the model an immutable surrogate key (such as a database ID or client GUID) for `[SparseKey]`, and keep the editable domain property as a regular property.

Keyed transitions compose with the rest of the library: element-wise rebase is described in [ChangeSet rebase](rebase.md#collection-and-structural-behavior), the wire envelope for keyed changes in [Change payload](change-payload.md#keyed-changes), and session acknowledgement in [UI frameworks](ui-frameworks.md#choosing-how-to-acknowledge-server-state).
