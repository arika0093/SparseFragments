# Cloning and Ownership

Values assigned directly to a Fragment or Patch are normally kept by reference. `Fragment.From(model)` copies supported model state, and `DeepClone()` creates an explicit independent copy.

For mutable objects such as lists, mutating a shared value after assignment also changes the value observed through the Fragment or Patch.

Concretely, assigning a mutable value to a patch shares it by reference. Nothing is cloned on assignment or on `Apply`:

<!-- illustrative: excerpt; uses guide-local names and does not compile as written -->
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
| `Fragment.From(model)` | Snapshots: isolated copy, except members marked `[SparseCloneReferenceSafe]`, which keep their references |
| Sparse construction (`new X.Fragment { ... }`) | Shares: assigned values are held by reference |
| `Merge`, especially `Replace` | Shares: the winning layer's reference is kept |
| Typed patch assignment (`new X.Patch { … }`) | Shares: nothing is cloned on assignment or on `Apply` |
| `Patch.Apply` | Shares: the result aliases the patch's assigned values |
| `ApplyChanges` | Shares (same rule as `Merge`/`Apply`) |
| `ToModel` | Shares: creates a new root model, but aliases fragment member references |
| `DeepClone` | Snapshots, except members marked `[SparseCloneReferenceSafe]`, which stay shared |
| Whole-contribution `Set(model)` | Snapshots (goes through `From`, preserving `[SparseCloneReferenceSafe]` references) |
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
[`SPF026`](../reference/analyzer.md#spf026-in-place-submit-is-unavailable).

## `DeepClone`

`DeepClone` structurally clones supported models and fragments. Members marked `[SparseCloneReferenceSafe]` are carried over by reference; everything else becomes independent:

<!-- illustrative: excerpt; uses names from the surrounding prose and does not compile as written -->
```csharp
var clone = original.ToModel().DeepClone();  // or fragment.DeepClone()
clone.Child!.Count = 42;                     // the original is untouched
```

* `Shared references are preserved within the cloned graph.` If two members of the source alias the same instance, the corresponding members of the clone alias one shared copy. The graph shape survives cloning.
* `Cycles are supported.` Object cycles in the source become equivalent cycles in the clone rather than infinite recursion.
* `[SparseCloneReferenceSafe].` Members that intentionally share references (services, caches, other out-of-graph singletons) should be marked so the cloner carries the reference over instead of requiring a deep-cloneable shape:

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
[SparseModel]
public partial class Settings
{
    [SparseMerge(MergeMode.Replace)]
    [SparseCloneReferenceSafe]
    public SharedService Service { get; set; } = null!;
}
```

`[SparseCloneReferenceSafe]` controls cloning policy and does not substitute for an explicit atomic merge mode (`[SparseMerge(MergeMode.Replace)]`) on non-partial custom reference types.

* Members with reference shapes that cannot be cloned safely (unsupported types, constructor-bound cycles) are generator errors ([`SPF008`](../reference/analyzer.md#spf008-unsupported-deep-clone-member)); either switch to a supported structural type/collection or mark the property `[SparseCloneReferenceSafe]`.

## Graph and Cycle Behavior

Not every operation accepts cyclic object graphs:

* `Fragment.From` and `Fragment.Diff` do **not** support cyclic object graphs. Shared (non-cyclic) references are allowed, but a cycle throws `NotSupportedException` naming the member path instead of overflowing the stack.
* `DeepClone` **does** support cycles and preserves shared references, as described above.
* Granular keyed-collection edits allocate a new container but still share element references; clone elements (or the whole graph via `DeepClone`) when the result must be independent.

Keep graphs acyclic at the `From`/`Diff` boundary. Use `DeepClone` when the result must not share mutable references with its source.

## Child-session fork cost

Opening a child session forks its parent session. The fork snapshots the current model as a baseline fragment and separately clones the live model; the child session also clones its own baseline. This keeps child edits isolated until acceptance and lets acceptance rebase them onto the parent's current model. The snapshot therefore covers the full root, including members outside the selected child path.

The root snapshot preserves the fork's independent ownership and rebase semantics. A child-only clone would need another way to isolate the fork's root state and apply the child's rebased changes to the parent's latest state. The measurements below show the cost of the current approach, including for a small child in a large root. They do not compare an alternative snapshot design.

The `EditSessionChildCloneBenchmarks` benchmark creates a fresh root edit session for each invocation. Its root has a `Target` group containing four servers and a keyed `Siblings` list of 0, 1,024, or 8,192 servers. `OpenSmallChildSession` opens `Target`; `OpenNestedChildSession` opens `Target` and then `Servers["target-1"]`. `OpenManyChildSessions` opens and disposes one or eight child sessions sequentially, using keyed siblings when present and `Target` when the list is empty. Each sibling server has an ID, name, and count.

The run used BenchmarkDotNet 0.15.8 with `ShortRun` (three measured iterations, three warmups, one launch), the .NET 10.0.401 SDK, and .NET 10.0.12 runtime. The host was Ubuntu 24.04.5 in a container on an Intel Core Ultra 9 285K at 4.60 GHz; BenchmarkDotNet reported 24 logical cores and one physical core. The benchmark ran on commit `fdcc0b2e9daa56a6681cea35848e21628a77f492` with RyuJIT x86-64-v3 and concurrent workstation GC.

Run the same benchmark type and selector from the repository root with:

```sh
dotnet run -c Release --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj -- --filter '*EditSessionChildCloneBenchmarks*' --job short
```

| Benchmark | Sibling count | Child sessions | Mean | Allocated per operation |
| --- | ---: | ---: | ---: | ---: |
| `OpenSmallChildSession` | 0 | 1 | 4.095 μs | 11.53 KB |
| `OpenSmallChildSession` | 8,192 | 1 | 3,443.013 μs | 4,136.01 KB |
| `OpenSiblingChildSessionAndDiscard` | 8,192 | 1 | 6,033.500 μs | 6,818.65 KB |
| `OpenNestedChildSession` | 0 | 1 | 10.348 μs | 30.53 KB |
| `OpenNestedChildSession` | 8,192 | 1 | 3,264.091 μs | 4,155.02 KB |
| `OpenManyChildSessions` | 0 | 8 | 22.586 μs | 66.44 KB |
| `OpenManyChildSessions` | 8,192 | 1 | 2,782.264 μs | 4,145.28 KB |
| `OpenManyChildSessions` | 8,192 | 8 | 17,698.752 μs | 23,081.68 KB |

The short run is directional: it used one process launch and three measured iterations, and the reported timing intervals were wide for the large-root cases. The allocation results still show the root-size and repeated-open costs. An invocation that creates a root session and opens the four-server target with 8,192 siblings allocated 4,136.01 KB; the eight-child invocation allocated 23,081.68 KB. The current root fork remains in place to preserve isolation and rebasing behavior. Reducing this cost requires a snapshot strategy that preserves those semantics.
