# Keyed Collections

SparseFragments can patch a collection of structural elements *by element* instead of replacing the whole collection — but only when the element type has a stable identity. This page explains when a collection is atomic versus keyed/structural, how to declare identity, and the resulting add/remove/edit/reorder semantics.

Related pages: [Merge strategies](merge-strategies.md) (whole-collection `Append` / `SetUnion` alternatives), [Patch rebase](rebase.md), [JSON Patch](json-patch.md) (positional vs keyed identity), [Model shapes](model-shapes.md) (promotion rules), [Diagnostics](analyzer.md) (`SPF011`–`SPF020`).

## Atomic vs Keyed Collections

| Collection kind | Examples | Patch semantics |
| --- | --- | --- |
| Scalar/atomic sequence | `List<string>`, `int[]` | Whole value: a patch sets or unsets the entire collection |
| Dictionary/map | `Dictionary<string, int>` | Keyed by `TKey` inherently; entries patch by key |
| Structural sequence **with** a key | `List<Server>` where `Server` declares `[SparseKey]` | Keyed: add/remove/edit by element, reorder by key order |
| Structural sequence **without** a key | `List<Server>` with no key declared | Generator error (`SPF011`): declare a key, or opt into `Append`, `SetUnion`, or a custom strategy on the member |

"Structural" here means the element type is a fragment model (or a reachable partial type eligible for promotion — see [Model shapes](model-shapes.md)). A sequence of scalars is never structural, no matter what merge mode is configured.

When no key is available and per-element patching is not needed, keep whole-collection semantics explicitly:

```csharp
[SparseFragmentModel]
public partial class Settings
{
    [SparseMerge(MergeMode.Append)]
    public List<AuditEntry> History { get; set; } = new();
}
```

## Declaring Identity

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

The generated composite key is a strongly typed tuple of the component values in declaration order, compared component-wise. Component order is significant: `(tenant, id)` and `(id, tenant)` are different key shapes. Marking more than one property with `[SparseKey]` is *not* a composite key (`SPF013`) — use the type-level form instead.

### `ISparseKeyed<TKey>` escape hatch

When identity cannot be expressed as a key property or an ordered composite — for example a normalized or case-folded key — implement `ISparseKeyed<TKey>`:

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

Keys must be stable and comparable:

* non-nullable (`string?`, `int?`, … are rejected, `SPF018`);
* not collection-shaped (arrays, `List<T>`, dictionaries, sets are rejected, `SPF019`);
* publicly readable instance properties — static, indexer, or non-publicly-readable properties cannot serve as identity (`SPF017`);
* value-object keys (records, record structs, enums, strings, GUIDs, ordinary scalars) are valid when they provide stable equality.

## Add / Remove / Edit / Reorder

`Patch.Between` derives per-element operations from the before/after key sets; `Apply` replays them. The final key order — not positional moves — determines the resulting order.

```csharp
var before = State(Holder(Server("a", "A"), Server("b", "B")));
var after = State(Holder(Server("b", "B2"), Server("c", "C")));

var patch = KeyedServerHolder.Patch.Between(before, after);
var applied = patch.Apply(before);
// applied holds keys ["b", "c"]; "b" was edited in place, "a" removed, "c" added.
```

Concretely:

* **Add.** An element whose key exists only in `after` is added. Its full fragment state is carried by the patch.
* **Remove.** An element whose key exists only in `before` is removed by key.
* **Edit.** An element present in both is patched through its generated nested patch — only the changed members travel. Editing nested members, nested keyed collections, and whole-value members all compose through the normal fragment algebra.
* **Replace the whole collection.** Assigning a fresh collection to the member (a `Set` on the collection member itself) replaces the container wholesale rather than diffing elements.
* **Reorder.** The resulting order is the final key order. Reversing `["a", "b"]` to `["b", "a"]` is a real (non-empty) patch whose replay reproduces the new order; there is no separate "move identity".

Keyed collections compose recursively: a keyed element type may itself hold keyed collections (for example teams holding keyed members), and each level diffs by its own keys.

## Duplicate Keys and Key Changes

* **Duplicate keys are invalid.** A collection state containing the same key twice has no well-defined element identity; deriving a patch from or onto such a state throws `InvalidOperationException`.
* **Changing an element's identity is remove-old + add-new.** If an edit changes the key property itself (for example renaming `Id` from `"a"` to `"b"`), the result is the removal of `"a"` plus the addition of `"b"` — never a silent retargeting of the edit onto a different element. State that would require retargeting round-trips as remove + add through `Between`/`Apply`.

## Interaction With Other Features

* **`Patch.Between` / `Apply`.** The primary surface: diffs are keyed, replays reproduce the exact after-state (a follow-up `Between(applied, after).IsEmpty` holds).
* **Composition / inversion.** Keyed patches compose (`second.Compose(first)`-style chaining) and invert relative to a baseline like any other patch; inversion needs the baseline because removed elements must be restored from it.
* **Rebase.** Keyed members rebase element-wise where the keys line up; divergent per-key edits surface as structured conflicts. See [Patch rebase](rebase.md).
* **JSON Patch.** RFC 6902 arrays are positional while keyed collections are identity-based. Import (`FromJsonPatch`) applies positional operations to the baseline's canonical JSON and then converts the outcome into a keyed semantic patch; export (`ToJsonPatch`) diffs before/after canonical JSON. The typed round-trip is semantic, not positional. See [JSON Patch](json-patch.md).
* **Merge.** Keyed structural members merge with the member's configured mode; whole-collection modes (`Append`, `SetUnion`) and custom strategies bypass keyed diffing for that member. See [Merge strategies](merge-strategies.md).
* **Ownership.** Granular keyed edits allocate a new container but share element references; see [Clone & ownership](cloning-and-ownership.md).

## Diagnostics

Key declaration problems are generator errors with dedicated diagnostics; the full reference lives in [Diagnostics](analyzer.md):

* `SPF011` — structural sequence without a usable key;
* `SPF012` — more than one key mechanism on the same type;
* `SPF013` — multiple property-level `[SparseKey]` markers;
* `SPF014` — invalid declaration shape (arguments on property-level use, empty type-level list);
* `SPF015`/`SPF016` — missing or duplicated composite component;
* `SPF017`/`SPF018`/`SPF019` — inaccessible, nullable, or collection-shaped key;
* `SPF020` — invalid or ambiguous `ISparseKeyed<TKey>` implementation.
