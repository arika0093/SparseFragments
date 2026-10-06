# JSON Patch

The generated `Patch.FromJsonPatch` / `ToJsonPatch` bridge converts between typed patches and standard [RFC 6902](https://datatracker.ietf.org/doc/html/rfc6902) JSON Patch documents. It is a **process-boundary interoperability format**: accept JSON Patch at the edge (HTTP PATCH endpoints, other services, stored JSON), work with it as a typed semantic patch in-process, and export it back out. In-process code should normally use the typed generated patch API directly.

Related pages: [Keyed collections](keyed-collections.md) (keyed vs positional identity), [Clone & ownership](cloning-and-ownership.md) (import snapshotting), [Model shapes](model-shapes.md).

## When to Use the Bridge

* Accepting standard JSON Patch documents from clients that know nothing about SparseFragments.
* Sending minimal change documents to non-.NET consumers.
* Storing or logging changes as portable JSON.

Everything else — merge, diff, apply, rebase, edit sessions — operates on typed fragments and patches without JSON involved.

## Import a JSON Patch Document

`Patch.FromJsonPatch` applies an RFC 6902 document to the canonical JSON of a baseline fragment, then derives the equivalent typed semantic `Patch`:

```csharp
using System.Text;
using SparseFragments;

var baseline = new Settings.Fragment { Label = "base" };
var document = Encoding.UTF8.GetBytes(
    """[{"op":"replace","path":"/Label","value":"patched"}]""");

var patch = Settings.Patch.FromJsonPatch(
    Optional<Settings.Fragment?>.Present(baseline),
    document);

var updated = baseline.Apply(patch);
// updated.Label == "patched"
```

The baseline is presence-aware, so the mapping is exact:

* `replace` with a JSON `null` value becomes a present null; `remove` becomes absent (`Missing`).
* A nested `add` fails when its parent fragment is absent (`JsonPatchErrorKind.MissingParent`).
* Whole-contribution transitions use the root pointer `""`: `add` from `Optional<Fragment?>.Missing`, `replace` with `null`, and `remove` to absent.
* Array element operations (`/Tags/1`, `/Tags/-`) work on import; collections are whole values on export (see below).
* Pointers follow RFC 6901 (`~0` for `~`, `~1` for `/`) and honor `[JsonPropertyName]` wire names, `JsonSerializerOptions.PropertyNamingPolicy`, and `PropertyNameCaseInsensitive`.

An overload taking a present `Fragment` directly (`FromJsonPatch(baseline, document, options)`) covers the common case. If a model happens to declare members named `FromJsonPatch` / `ToJsonPatch`, the bridge is emitted with a `Sparse` prefix instead (`SparseFromJsonPatch` / `SparseToJsonPatch`).

Imported values are freshly deserialized, so the resulting patch owns isolated copies — see [Clone & ownership](cloning-and-ownership.md).

## Export a Typed Patch

`ToJsonPatch` runs the typed patch against the same baseline and diffs the before/after canonical JSON:

```csharp
var baselineOpt = Optional<Settings.Fragment?>.Present(baseline);
var exported = patch.ToJsonPatch(baselineOpt); // ReadOnlyMemory<byte>, UTF-8 JSON
Console.WriteLine(Encoding.UTF8.GetString(exported.ToArray()));
// [{"op":"replace","path":"/Label","value":"patched"}]
```

Export is semantic, not a verbatim replay of the import:

| Input shape | Exported shape |
| --- | --- |
| Objects | Diffed recursively member by member |
| Arrays and scalars | Collapsed to a whole-value `add` / `remove` / `replace` on the member path |
| `move` / `copy` | Collapsed to the equivalent `remove` plus `add` / `replace` |
| `test` | Validation-only; never appears in the export |

Round-tripping holds **semantically**: applying the re-imported export to the same baseline produces the same fragment as applying the original typed patch — even though the exported operations may differ textually from the imported ones.

## Keyed Collections and Positional Identity

RFC 6902 arrays are positional (`/Items/0`), while keyed structural collections are identity-based (see [Keyed collections](keyed-collections.md)). The bridge reconciles the two models through the baseline:

* On **import**, positional operations apply to the baseline's canonical array order, and the outcome is converted into a keyed semantic patch. Positional edits to a keyed collection therefore work, but they are interpreted against the baseline's order at import time.
* On **export**, the before/after canonical JSON is diffed, so keyed add/remove/edit/reorder appears as array-level operations against the baseline order.

Prefer the typed `Patch.Between` / `Apply` API for keyed collections whenever both sides are in-process; reserve the bridge for the boundary.

## Options, Converters, and NativeAOT

Both directions accept an optional `JsonSerializerOptions`:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
};

// Scalar/collection members resolve through options.TypeInfoResolver.
var patch = Settings.Patch.FromJsonPatch(baselineOpt, document, options);
var exported = patch.ToJsonPatch(baselineOpt, options);
```

The fragment itself never needs `JsonTypeInfo` metadata: conversion goes through the generated `Fragment.FragmentJsonConverter` directly. Only scalar/collection member types use the supplied resolver, so NativeAOT applications just pass a source-generated context:

```csharp
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class PatchContext : JsonSerializerContext;
```

```csharp
var options = new JsonSerializerOptions { TypeInfoResolver = PatchContext.Default };
```

When reflection-based serialization is disabled and no resolver is supplied, the bridge fails fast with a clear `InvalidOperationException` instead of reaching runtime codegen. `Fragment.FragmentJsonConverter` is also public, so ordinary `JsonSerializer.Serialize(fragment, options)` works with the same presence semantics (present members only, explicit nulls preserved).

## Failures Are Typed

Malformed documents, unknown operations, bad pointers, missing targets/parents, invalid array indices, failed `test` operations, unmapped properties, and member deserialization failures all throw `JsonPatchException` with a machine-readable `Kind`:

```csharp
try
{
    var patch = Settings.Patch.FromJsonPatch(baselineOpt, document);
}
catch (JsonPatchException ex) when (ex.Kind == JsonPatchErrorKind.MissingTarget)
{
    // e.g. replace/remove/test on a path that does not exist in the baseline.
}
```

| `JsonPatchErrorKind` | Meaning |
| --- | --- |
| `MalformedDocument` | The patch document is not a valid JSON Patch array |
| `UnknownOperation` | The operation name is unknown |
| `MalformedPointer` | A JSON Pointer is malformed |
| `UnmappedProperty` | A JSON property does not map to a fragment member |
| `MissingTarget` | A remove, replace, or test target does not exist |
| `MissingParent` | A parent required for a nested add does not exist |
| `InvalidArrayIndex` | An array index is invalid |
| `TestFailed` | A test operation failed |
| `DeserializationFailed` | A value could not be deserialized to a fragment member |
