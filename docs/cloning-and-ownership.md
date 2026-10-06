# Cloning and Ownership

Values assigned directly to a Fragment or Patch are normally kept by reference. `Fragment.From(model)` copies supported model state, and `DeepClone()` creates an explicit independent copy. This matters for mutable objects such as lists: mutating a shared value after assignment also changes the value observed through the Fragment or Patch.

Concretely, assigning a mutable value to a patch shares it by reference — nothing is cloned on assignment or on `Apply`:

```csharp
var tags = new List<string> { "a" };
var patch = new Settings.Patch { Plugins = tags };
var result = new Settings.Fragment().Apply(patch);

tags.Add("b"); // visible through result.Plugins and patch.Plugins: one shared list.
```

Do not mutate a shared object after assigning it if the Fragment/Patch must remain stable. Clone the value first when independent ownership is required. When a patch value must stay independent, clone it before assigning (`model.DeepClone()` / `fragment.DeepClone()`) and leave the source alone afterwards.

## Operation / Ownership Table

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
| JSON Patch import | Snapshots (freshly deserialized values) |
| Granular keyed-collection edits | New container, shared element references |

SparseFragments does not clone every assigned value during `Merge` or `Apply`, so those operations may reuse caller-provided references.

The original fragment is never mutated (`Apply` builds a new one), but the patch, the assigned source value, and the result alias the same instance. When a patch value must stay independent, clone it before assigning and leave the source alone afterwards.

## `DeepClone`

`DeepClone` structurally clones supported models and fragments. Members marked `[SparseCloneReferenceSafe]` are carried over by reference; everything else becomes independent:

```csharp
var clone = original.ToModel().DeepClone();  // or fragment.DeepClone()
clone.Child!.Count = 42;                     // the original is untouched
```

* **Shared references are preserved within the cloned graph.** If two members of the source alias the same instance, the corresponding members of the clone alias one shared copy — the graph shape survives cloning.
* **Cycles are supported.** Object cycles in the source become equivalent cycles in the clone rather than infinite recursion.
* **`[SparseCloneReferenceSafe]`.** Members that intentionally share references (services, caches, other out-of-graph singletons) should be marked so the cloner carries the reference over instead of requiring a deep-cloneable shape:

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
