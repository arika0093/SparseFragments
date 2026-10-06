# Model Shapes

Annotate the model with `[SparseFragmentModel]`:

```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public Child? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}
```

Per-member sparse behavior (merge, nested diff/patch, keyed identity) needs generated
APIs emitted *into the member's type*. That is only possible when the type is
declared `partial` so the generator can append to it. When the type cannot carry
generated APIs, the member cannot diff inside the value — it stays whole-value, and
the generator requires you to say so explicitly rather than guessing.

The root model must be:

* declared `partial` (the generator appends `Fragment` / `Patch` members to the same type; `SPF001`);
* a top-level, non-generic, non-`abstract` class or struct — nested types, generic types, `abstract` types, interfaces, `ref` structs, and `file`-local types are unsupported (`SPF002`);
* constructible: a class model needs a parameterless constructor or a constructor whose parameters match the public readable properties by name and type (a corresponding setter, when present, must be `public`) (`SPF003`).

Members may be readable/writable or init-only as applicable; `required` members must be representable as accessible public properties in the fragment construction plan (a `public` setter, or `init` covered by constructor binding), otherwise generation fails (`SPF006`).

## Nested Structural Models and Promotion

Reachable partial nested model types automatically receive generated Fragment/Patch APIs — a nested type does **not** need its own `[SparseFragmentModel]` annotation when it is reachable and structurally eligible:

```csharp
public partial class Child   // no annotation needed: promoted automatically
{
    public int Count { get; set; }
    public string Host { get; set; } = "localhost";
}
```

The rules:

* **Make the nested type `partial`** when it needs independently sparse behavior (per-member merge, nested diff/patch, keyed identity). The generator promotes reachable partial nested types to first-class fragments.
* **Non-partial nested POCOs do not implicitly participate in sparse/deep semantics.** A nested type that is not `partial` cannot carry generated APIs, so the member cannot diff inside the value. Declare the nested type `partial` to enable structural sparse behavior, or explicitly mark the member with `[SparseMerge(MergeMode.Replace)]` to treat it as an atomic value. Otherwise the generator reports `SPF007` — atomic replacement is always an explicit opt-in, never a silent fallback:

```csharp
public class Child
{
    public string? Name { get; set; }
}

[SparseFragmentModel]
public partial class Parent
{
    public Child Child { get; set; } = new(); // SPF007
}
```

```csharp
public partial class Child { ... } // promoted structural model
```

```csharp
[SparseMerge(MergeMode.Replace)]
public Child Child { get; set; } = new(); // explicit atomic replacement
```
* **Shared nested types must agree.** A nested type without its own explicit `[SparseFragmentModel]` root that is referenced from multiple roots with different generated semantics (different member sets or merge settings) is an error (`SPF010`): unify the definitions and settings, or annotate the nested type itself with `[SparseFragmentModel]` to promote it to an explicit root.
* **Structural collection element types are discovered the same way.** A `List<T>` member whose `T` is a fragment model (or a promotable partial) is a structural sequence and needs key identity (see [Keyed collections](keyed-collections.md)); otherwise the member needs `Append`, `SetUnion`, or a custom strategy.

Element identity for structural collections is declared with `[SparseKey]` (single property or ordered type-level composite) or `ISparseKeyed<TKey>` — exactly one mechanism per structural type. Declaration problems surface as `SPF011`–`SPF020`.

## Constructors, Members, and Cycles

* Prefer a parameterless constructor; constructor-bound properties (including `init` and `required`) resolve by name-and-type matching.
* Members marked `[JsonIgnore]` never participate in JSON conversion; members that would collide on the same JSON wire name fail during analysis (`SPF021`) rather than at runtime.
* Members named `JsonConverter` or `FragmentJsonConverter` collide with the generated JSON bridge (`SPF009`): rename the member.
* `From` / `Diff` reject cycles with a path-naming `NotSupportedException`, while `DeepClone` preserves them — see [Clone & ownership](cloning-and-ownership.md).
