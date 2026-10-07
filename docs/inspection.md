# Inspection

Generic code often needs to answer two questions without knowing a model's members in advance: how does SparseFragments interpret this model, and what did one particular `Patch` change? `T.Sparse.Properties` answers the first; `patch.Changes` answers the second. Both are generated at compile time, read-only, and require no reflection.

<!-- sample: inspection-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class Roster
{
    public string Name { get; set; } = string.Empty;

    public List<Quest> Quests { get; set; } = new();
}

public partial class Quest
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
```
<!-- /sample -->

`T.Sparse.Properties` describes how SparseFragments interprets the model; `patch.Changes` describes which of those semantic properties are changed in a particular `Patch`:

<!-- sample: inspection-first -->
```csharp
var before = Roster.Fragment.From(new Roster
{
    Quests = new() { new Quest { Id = "a", Title = "Old" } },
});
var after = Roster.Fragment.From(new Roster
{
    Quests = new() { new Quest { Id = "a", Title = "New" } },
});

foreach (var property in Roster.Sparse.Properties)
{
    Console.WriteLine(property.Name);
}
// Name
// Quests

var patch = Roster.Patch.Between(before, after);
foreach (var change in patch.Changes)
{
    Console.WriteLine(change.Property.Name);
}
// Quests
```
<!-- /sample -->

If `Patch` basics are unfamiliar, start with [Fragments and patches](fragments-and-patches.md).

## Static Model Metadata

`T.Sparse.Properties` is a fixed `IReadOnlyList<SparsePropertyInfo>` with one immutable descriptor per model property, in property-name order. Each descriptor carries the SparseFragments reading of its member:

* `Name` and `PropertyType`, plus `IsNullable` for the declared nullability;
* `IsNestedModel` and `NestedModelType` for members that carry their own Fragment/Patch;
* `MergeMode` and `MergeStrategyType` for the merge behavior (`Replace`, `Deep`, `Append`, `SetUnion`, `Custom`);
* `CollectionKind` (`None`, `Array`, `List`, `Set`, `Dictionary`) and `CollectionSemantic` (`None`, `ScalarSequence`, `KeyedSequence`, `Dictionary`);
* `KeyKind` (`None`, `Property`, `Composite`, `Interface`), `KeyPropertyNames`, and `KeyType` for keyed elements;
* `JsonPropertyName` and the `IsRequired` / `IsInitOnly` / `IsReadOnly` flags.

This is semantic metadata, not CLR reflection. At the CLR level `List<string>` and `List<Quest>` are both lists; SparseFragments treats the first as one atomic value and the second as individually patchable elements:

<!-- sample: inspection-kinds -->
```csharp
var byName = Roster.Sparse.Properties.ToDictionary(static property => property.Name);

var quests = byName["Quests"];
// quests.CollectionSemantic == SparseCollectionSemantic.KeyedSequence
// quests.KeyKind == SparseKeyKind.Property
// quests.KeyPropertyNames == ["Id"]

var name = byName["Name"];
// name.CollectionSemantic == SparseCollectionSemantic.None
// name.IsNestedModel == false
```
<!-- /sample -->

Enumerate `T.Sparse.Properties` for generic handling. There are no typed per-property handles such as `T.Sparse.Properties.Title`: the first version intentionally supports enumeration, not individual property lookup by member.

A model member or nested type named `Sparse` collides with the generated holder and fails with [`SPF009`](analyzer.md#spf009-member-conflicts-with-generated-json-patch-api): rename the member.

## Patch Changes

`patch.Changes` is the read-only semantic view of a generated `Patch`: the non-empty member changes in that patch instance. A whole-patch `Set`/`Unset` applies to the entire model rather than one property, so it is excluded; only member changes are enumerated.

Each `SparsePatchChange` references the corresponding `T.Sparse.Properties` descriptor through `change.Property` — the same instance, so descriptors and changes share one vocabulary for property identity. `change.Kind`, a `SparseChangeKind`, distinguishes `Set`, `Unset`, nested structural patches (`Nested`), granular keyed collections (`KeyedCollection`), and granular dictionaries (`Dictionary`). Scalar members report `Set` with the boxed value in `change.Value` (including an explicit `null`), or `Unset`.

A nested structural change carries its own changes recursively, so generic code walks root patch, changed property, nested patch, changed property without knowing every generated `Patch` type:

<!-- sample: inspection-nested -->
```csharp
var editBefore = Roster.Fragment.From(new Roster
{
    Quests = new() { new Quest { Id = "a", Title = "Old" } },
});
var editAfter = Roster.Fragment.From(new Roster
{
    Quests = new() { new Quest { Id = "a", Title = "New" } },
});

var editPatch = Roster.Patch.Between(editBefore, editAfter);
var questsChange = editPatch.Changes.Single(static change => change.Property.Name == "Quests");
var edit = questsChange.Keyed!.Edited.Single();
foreach (var nested in edit.NestedChanges)
{
    Console.WriteLine(nested.Property.Name);
}
// Title
```
<!-- /sample -->

Direct nested members work the same way: a `Nested` change exposes the child patch's changes through `NestedChanges`, while a whole nested replacement reports `Set` or `Unset` instead.

## Keyed Collection Changes

For a keyed structural collection the inspection exposes the semantics SparseFragments already computed: which elements were added, which keys were removed, which keys were edited (with each edit's nested property changes), and the final key order. The vocabulary matches [Keyed collections](keyed-collections.md): identity is by stable key, a key change is remove-old plus add-new, and reorder is final key order rather than synthetic move operations.

<!-- sample: inspection-keyed -->
```csharp
var keyedBefore = Roster.Fragment.From(new Roster
{
    Quests = new() { new Quest { Id = "a", Title = "A" }, new Quest { Id = "b", Title = "B" } },
});
var keyedAfter = Roster.Fragment.From(new Roster
{
    Quests = new() { new Quest { Id = "b", Title = "B2" }, new Quest { Id = "c", Title = "C" } },
});

var keyedPatch = Roster.Patch.Between(keyedBefore, keyedAfter);
var keyedChange = keyedPatch.Changes.Single(static change => change.Property.Name == "Quests");
var keyed = keyedChange.Keyed!;
foreach (var added in keyed.Added)
{
    Console.WriteLine(((Quest)added!).Id);
}
// c
foreach (var removed in keyed.RemovedKeys)
{
    Console.WriteLine((string)removed!);
}
// a
// keyed.Edited holds per-key nested changes (see above)
// keyed.HasOrder reports whether the final order changed
// keyed.KeyOrder holds the final key order when it did
```
<!-- /sample -->

Scalar sequences stay whole-value operations: changing one `Scores` element reports `Set` with the replacement list, exactly as the patch itself behaves. Replacing a whole keyed collection with `Set` likewise reports `Set`, which keeps whole replacement distinct from granular add/remove/edit operations.

## Practical Uses

Once the two APIs above are in place, the same inspection code serves several consumers:

* generic diff and change viewers that list what changed without model-specific branches;
* audit and log output that records changed properties and keys;
* admin and configuration tooling that previews a patch before applying it;
* generic editors and UI highlighting (see [UI frameworks](ui-frameworks.md) for framework binding);
* debugging and diagnostics of unexpected patch contents.

The Playground roster editor consumes `T.Sparse.Properties` and `patch.Changes` this way to highlight added, removed, edited, and moved rows; it is a live example of the workflow without extra documentation here.

## Reflection, Trimming, and NativeAOT

The metadata and change views are generated from the compile-time SparseFragments analysis of each model. Callers never rediscover the model through reflection or JSON conversion: type identity uses `typeof`, collection and key semantics arrive as enums and key names, and enumeration projects the existing patch state. Inspecting a patch allocates only small view objects, and constructing or applying a patch costs nothing extra when inspection is never used.
