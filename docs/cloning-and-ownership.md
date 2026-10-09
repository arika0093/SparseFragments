# Cloning and Ownership

Values assigned directly to a Fragment or Patch are normally kept by reference. `Fragment.From(model)` copies supported model state, and `DeepClone()` creates an explicit independent copy.

For mutable objects such as lists, mutating a shared value after assignment also changes the value observed through the Fragment or Patch.

Concretely, assigning a mutable value to a patch shares it by reference. Nothing is cloned on assignment or on `Apply`:

```csharp
var tags = new List<string> { "a" };
var patch = new Settings.Patch { Plugins = tags };
var result = new Settings.Fragment().Apply(patch);

tags.Add("b"); // visible through result.Plugins and patch.Plugins: one shared list.
```

Do not mutate a shared object after assigning it if the Fragment or Patch must remain stable. Clone the value first when independent ownership is required. When a patch value must stay independent, clone it before assigning (`model.DeepClone()` or `fragment.DeepClone()`) and leave the source alone afterwards.

## Operation and Ownership Table

| Operation | Shares or snapshots? |
| --- | --- |
| `Fragment.From(model)` | Snapshots: the fragment graph is an isolated copy |
| Sparse construction (`new X.Fragment { ... }`) | Shares: assigned values are held by reference |
| `Merge`, especially `Replace` | Shares: the winning layer's reference is kept |
| Typed patch assignment (`new X.Patch { … }`) | Shares: nothing is cloned on assignment or on `Apply` |
| `Patch.Apply` | Shares: the result aliases the patch's assigned values |
| `ApplyChanges` | Shares (same rule as `Merge`/`Apply`) |
| `ToModel` | Shares: the model aliases fragment member references |
| `DeepClone` | Snapshots, except members marked `[SparseCloneReferenceSafe]`, which stay shared |
| Whole-contribution `Set(model)` | Snapshots (goes through `From`) |
| ChangeSet payload deserialization | Snapshots (freshly deserialized values) |
| Granular keyed-collection edits | New container, shared element references |

As summarized above, `Merge` and `Apply` do not clone assigned values, reusing caller-provided references instead. While `Apply` constructs and returns a new fragment rather than mutating the original, the resulting fragment shares reference-type member instances with the patch and the assigned source.

## In-place application

For writable reference-type models, generated `Fragment.WriteTo(model)` and
`Patch.ApplyInPlace(model)` preserve the
root model identity. A `List<T>` or `Dictionary<TKey,TValue>` property also
preserves its existing collection instance while replacing its contents.
Other members are assigned from the computed result, so nested model objects
may be replaced. These APIs mutate caller-owned state and should be used only
when that identity-preserving behavior is desired. Init-only or get-only
members disable in-place APIs; see
[`SPF026`](analyzer.md#spf026-in-place-submit-is-unavailable).

## `DeepClone`

`DeepClone` structurally clones supported models and fragments. Members marked `[SparseCloneReferenceSafe]` are carried over by reference; everything else becomes independent:

```csharp
var clone = original.ToModel().DeepClone();  // or fragment.DeepClone()
clone.Child!.Count = 42;                     // the original is untouched
```

* `Shared references are preserved within the cloned graph.` If two members of the source alias the same instance, the corresponding members of the clone alias one shared copy. The graph shape survives cloning.
* `Cycles are supported.` Object cycles in the source become equivalent cycles in the clone rather than infinite recursion.
* `[SparseCloneReferenceSafe].` Members that intentionally share references (services, caches, other out-of-graph singletons) should be marked so the cloner carries the reference over instead of requiring a deep-cloneable shape:

```csharp
[SparseFragmentModel]
public partial class Settings
{
    [SparseCloneReferenceSafe]
    public SharedService Service { get; set; } = null!;
}
```

* Members with reference shapes that cannot be cloned safely (unsupported types, constructor-bound cycles) are generator errors ([`SPF008`](analyzer.md#spf008-unsupported-deep-clone-member)); either switch to a supported structural type/collection or mark the property `[SparseCloneReferenceSafe]`.

## Graph and Cycle Behavior

Not every operation accepts cyclic object graphs:

* `Fragment.From` and `Fragment.Diff` do **not** support cyclic object graphs. Shared (non-cyclic) references are allowed, but a cycle throws `NotSupportedException` naming the member path instead of overflowing the stack.
* `DeepClone` **does** support cycles and preserves shared references, as described above.
* Granular keyed-collection edits allocate a new container but still share element references; clone elements (or the whole graph via `DeepClone`) when the result must be independent.

Keep graphs acyclic at the `From`/`Diff` boundary. Use `DeepClone` when the result must not share mutable references with its source.
