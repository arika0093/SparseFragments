# Descriptors reference

Session-bound runtime descriptors expose generated property metadata and observable-backed accessors from edit sessions. Framework code uses them to enumerate members, inspect CLR types and attributes, and edit supported sequences and dictionaries without reflection. Descriptors stay independent from sparse merge semantics.

Start from a session. `session.Descriptors.TryGet` looks a member up by its CLR name; the returned descriptor reads the live value, replaces it, and opens structural accessors for nested models and collections:

<!-- sample: ui-descriptor-models -->
```csharp
[SparseFragmentModel]
public partial class UiOrder
{
    public string Number { get; set; } = string.Empty;

    public List<UiOrderItem> Items { get; set; } = new();
}

public partial class UiOrderItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
```
<!-- /sample -->

<!-- sample: ui-descriptor-first -->
```csharp
var catalogModel = new UiOrder { Number = "a" };
var catalogSession = catalogModel.CreateEditSession();

var found = catalogSession.Descriptors.TryGet("Number", out var title);
// found == true
// title.Name == "Number"
// title.GetValue() is "a"
var metadata = (ISparsePropertyMetadata)title;
// metadata.DeclaredType == typeof(string)
// metadata.IsNullable == false

var renamed = title.TrySetValue("b");
// renamed == true
// catalogModel.Number == "b"

var items = catalogSession.Descriptors.TryGet("Items", out var itemsDescriptor);
// items == true
var added = itemsDescriptor.Array!.TryAdd(new UiOrderItem { Id = "a", Name = "first" });
// added == true
// catalogModel.Items.Count == 1
```
<!-- /sample -->

Three contracts meet in that one object: static metadata describes the declared member, instance access reads and writes the live model through the generated observable, and change inspection flattens transitions into rows for logs and lists:

<!-- sample: ui-descriptor-changes -->
```csharp
var logModel = new UiOrder { Number = "a" };
var logSession = logModel.CreateEditSession();
logSession.Observable.Number = "b";

var rows = logSession.CreateChangeSet().EnumerateChanges().ToList();
// rows.Count == 1
// rows[0].Path == "Number"
// rows[0].Kind reports an edited value
// logSession.EnumerateChangedPaths() lists "Number"
```
<!-- /sample -->

Handwritten per-member code should use the typed `Observable` surface instead. Descriptors back generic form builders, validation, and diagnostics.

## Static metadata

`ISparseModelMetadata` describes one generated model: its CLR type and its property entries in declaration order, with lookup by CLR name. `ISparsePropertyMetadata` describes one declared property: its CLR name, declared and view types, nullability, required state, editability, declared attributes, and shape. Both are instance-independent. They can be inspected without creating an observable, and the per-model generated code owns a single cached instance whose invariant entries are shared, never rebuilt per access.

`Type` is the declared model type used for construction. `ViewType` is the runtime type returned by current reads. They differ only where reads go through generated proxies.

| Member kind | Type | ViewType | Read result |
| --- | --- | --- | --- |
| Scalar, string, array | Declared type | Same as declared | Model value (arrays are live references) |
| Nested generated model | Child model | Child observable proxy | Proxy, or null |
| Observable list or dictionary | Declared collection | Observable view | View, or null |
| Read-only sequence or dictionary | Declared collection | Declared collection, or element proxy for fragment elements | Model values, transient proxies for fragment elements |
| Set | Declared set | Declared set | Live model values |

`TrySetValue` accepts model instances for every shape. Nested references and proxied collection elements additionally accept observable proxies, which are unwrapped to their targets. Array items report the model type in both `ItemType` and `ItemViewType` because arrays hold models.

Nullability has three states. `IsNullable` reports explicitly annotated nullability. `IsNullableOblivious` reports reference types compiled without nullable annotations; those accept null through `TrySetValue` like annotated nullable members, while explicitly non-nullable members reject null. `IsRequired` reports the C# `required` keyword for creation forms. It is independent of nullability and of validation attributes such as `RequiredAttribute`, which remain visible in `Attributes`. Editing existing instances behaves the same for required and optional members.

`Attributes` carries the declared attribute instances for the property, including validation attributes. Attribute arguments are limited to what the generator can materialize; unsupported arguments are omitted from the array rather than failing generation. The descriptor surface follows the same approval process as other generated APIs: intentional changes update the public API snapshots in the same change.

## Instance access

`ISparseInstanceAccess` reads and writes the live model through the generated observable: `GetValue`, `TrySetValue`, and the structural `Child`, `Array`, `Dictionary`, and `Set` properties. Each structural property returns an accessor bound to the current value, or null when the value is null.

`Shape` (`SparseDescriptorShape`) is static declared metadata that is always available, including for null members. `HasChild`, `HasArray`, `HasDictionary`, and `HasSet` report whether the shape is supported; the matching live accessor reports whether an instance exists right now. A nullable child or collection assigned later keeps its shape, and the live accessor appears after `TrySetValue`.

| Situation | Shape flags | Live accessor |
| --- | --- | --- |
| Unsupported shape (for example scalar `Child`) | All false | Null |
| Supported shape with a value | Matching flag true with types | Live accessor |
| Supported shape with null value | Matching flag true with types | Null |
| Nullable child or collection assigned later | Unchanged | Appears after `TrySetValue` |

## Change inspection

`ISparseChangeInspector` enumerates flattened `SparseChangeRecord` entries (path, presence-aware before/after, `SparseChangeKind`) in deterministic order without naming generated CLR types, so diff inspection tools work across models and assemblies. Per-model `ChangeSet.EnumerateChanges()` results project into this contract: flattened enumeration and `EnumerateChangedPaths()` are the model-bound seams, and ordinary editing reads the typed member transitions instead.

`Added`, `Removed`, and `Changed` classify value transitions, while `Order` uses the collection path and carries the before and after key sequences. Nested values use dotted paths; keyed entries use their key in brackets.

## Identity and lifetime

Descriptors are instance-bound, not path-bound. `IDescriptor.Path` records the creation-time location. A retained accessor does not follow replacement, and a write through it fails instead of editing a detached object. Re-resolve from the session or parent to obtain live accessors.

| Change | Retained accessor behavior |
| --- | --- |
| Child property replaced or nulled | Writes return false; structural accessors return null |
| Revert or reload that rebuilds instances | Same as replacement |
| Revert that edits in place | Stays live (same instance) |
| List move, insert, remove, item replacement | Item writes return false unless the index still resolves to the captured model |
| Duplicate references to one instance | Stay live for either position (both denote the same object) |
| Dictionary value replaced, removed, or reinserted | Value writes return false; missing keys report null accessors |
| Independent index or key changes | Other retained accessors stay live |

A retained accessor whose captured instance is no longer current is stale. Reads keep observing the captured instance. Writes return false and structural accessors return null. Stale accessors never mutate an orphan and never raise notifications.

## Collection capabilities

Mutators are exposed only where a supported notifying mutation route exists. `CanX` reflects the current backing instance and may change if the property is replaced.

| Declared shape | Descriptor | Reads | Mutations |
| --- | --- | --- | --- |
| `T[]` | Array, immutable | Count, GetItem, nested sets for fragment elements | None; child member edits notify the parent |
| `List<T>`, `IList<T>`, `Collection<T>`, `ObservableCollection<T>` | Array, mutable unless read-only or fixed size | Count, GetItem, nested sets for fragment elements | Add, insert, set, remove, move where the provider allows |
| `IReadOnlyList<T>` | Array, immutable | Live Count and GetItem; nested sets for fragment elements | None |
| `IReadOnlyCollection<T>`, `IEnumerable<T>` | Array, immutable | Point-in-time snapshot Count and GetItem; nested sets for fragment elements | None |
| `Dictionary<TKey, TValue>`, `IDictionary<TKey, TValue>` | Dictionary, mutable unless read-only | Count, Keys, TryGetValue, nested sets for fragment values | Add, set, remove where the provider allows |
| `SortedDictionary<TKey, TValue>`, `SortedList<TKey, TValue>`, `IReadOnlyDictionary<TKey, TValue>` (behind interface declarations) | Dictionary, immutable | Live Count, Keys, TryGetValue, nested sets for fragment values | None |
| `HashSet<T>`, `ISet<T>` | Set, mutable unless read-only | Live Count, Items, comparer-sensitive Contains | Add and remove where the backing set is a mutable set |
| `IReadOnlySet<T>` | Set, immutable unless the runtime set is mutable | Live Count, Items, Contains | Only when the runtime instance is a mutable set |

Keyed lists additionally expose `IsKeyed`, `KeyType`, `KeyPropertyNames`, `GetItemKey`, `IndexOfKey` and `IsUnassignedKey`, sourced from key analysis. Lookup is pure key equality: unassigned sentinels match like any other value, so callers that preserve multiples check `IsUnassignedKey` first. Scalar sequences report unkeyed with absent lookups.

## Try contract and atomicity

Unsupported edits (immutable backing, fixed size, missing instance, stale accessor) return false and change nothing. Invalid inputs (unconvertible value, duplicate assigned key, out-of-range index or key) return false before any mutation and raise no notification. Programmer and transport failures (for example I/O or serialization errors inside custom providers) propagate. Provider `NotSupportedException` from size operations reports false. Duplicate assigned keys are rejected for keyed add, insert and set with no mutation; unassigned sentinels are exempt and behave like direct observable mutation. Replacing the item at its own index with the same key succeeds.

## Observability and safety

Reads that expose raw mutable references (arrays, unproxied objects, sets) invalidate the session change cache through the raw-access callback, so a later in-place mutation is observed. Primitive, string, proxy and view reads stay cheap and do not invalidate. Invalidation recomputes on the next `HasChanges` without claiming a transition. Effective set and child member edits raise the parent property notification exactly once; rejected edits raise nothing. Transient element proxies for read-only and array shapes notify the parent property on child edits. Collection notifications are synchronous and do not marshal to a UI thread; callers are responsible for thread affinity.

## Path and identity

Paths join member names with dots, list positions with `[i]`, and dictionary keys with the canonical bracket form: the InvariantCulture key text, double-quoted, with backslash and quote escaping. All key types render quoted with no type prefix, matching change-set paths and Blazor field paths; typed lookup still distinguishes key types. Keyed-list identity comes from key analysis (`IsKeyed`, `KeyType`, key properties, unassigned marker); positional paths stay positional and always name the item a fresh lookup would modify.

## Intentionally unsupported

Sorted dictionary and sorted list types as declared member types fail in fragment write paths and therefore cannot be modeled. Nullable fragment elements in collections warn in the fragment clone path. Unassigned keyed entries cannot travel through live keyed transitions. Non-scalar members named `PropertyChanged` are readable but have no structural descriptors: scalar descriptors target the model with proxy notifications while other shapes stay omitted. Read-only collection snapshots do not follow later backing changes. Temporary invalid keyed states are rejected at the descriptor boundary rather than represented.
