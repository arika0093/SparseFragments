# Model Shapes

For the common case, add `[SparseFragmentModel]` to a top-level `partial` class or struct. Nested models should also be `partial` when you want SparseFragments to merge and patch their members independently.

```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public Child? Child { get; set; }
}
```

## Root Model Requirements

The root model must be:

* declared `partial` (the generator appends `Fragment` / `Patch` members to the same type; [`SPF001`](analyzer.md#spf001-sparse-fragment-model-must-be-partial));
* a top-level, non-generic, non-`abstract` class or struct — nested types, generic types, `abstract` types, interfaces, `ref` structs, and `file`-local types are unsupported ([`SPF002`](analyzer.md#spf002-unsupported-sparse-fragment-model));
* constructible: a class model needs a parameterless constructor or a constructor whose parameters match the public readable properties by name and type (a corresponding setter, when present, must be `public`) ([`SPF003`](analyzer.md#spf003-model-needs-a-supported-constructor)).

Members may be readable/writable or init-only as applicable. A `required` member must be assignable by the generated constructor path: either through a public setter or a matching constructor parameter. Otherwise generation fails ([`SPF006`](analyzer.md#spf006-required-member-cannot-be-constructed)).

Mark a property with `[SparseIgnore]` to remove it from the generated surface entirely. Ignored properties are omitted from `Fragment`, `Patch`, `ChangeSet`, `Observable`, clone/diff, and generated JSON. `ToModel()` leaves them at their model default; model-level `ApplyTo` and `TryApplyTo` carry their current values forward when the property has a public setter:

```csharp
[SparseFragmentModel]
public partial class Settings
{
    public string Name { get; set; } = "";

    [SparseIgnore]
    public string RuntimeSecret { get; set; } = "";
}
```

`[SparseIgnore]` can be inherited with a property declaration and is supported on promoted nested models. It cannot be used on a key or on required, init-only, or non-defaulted constructor-bound properties ([`SPF022`](analyzer.md#spf022-sparseignore-on-key), [`SPF023`](analyzer.md#spf023-sparseignore-on-unsupported-property)). It may be combined with `[JsonIgnore]`; `[JsonIgnore]` alone continues to affect generated JSON only.

Most members need no merge configuration: scalars and ordinary collections use `Replace`, while nested generated models use `Deep`. Add `[SparseMerge]` only when you want different behavior (see [Merge strategies](merge-strategies.md)):

```csharp
[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}
```

## Nested Models

To patch a nested model's members independently, declare the nested type `partial`. To treat a nested value atomically instead, mark the member with `[SparseMerge(MergeMode.Replace)]`.

Reachable `partial` nested types automatically receive generated Fragment/Patch support without their own `[SparseFragmentModel]` annotation; the generator refers to this as promotion:

```csharp
public partial class Child   // no annotation needed: promoted automatically
{
    public int Count { get; set; }
    public string Host { get; set; } = "localhost";
}
```

The rules:

* **Make the nested type `partial`** when it needs independent member-level behavior (per-member merge, nested diff/patch, keyed identity).
* **Non-partial nested types stay atomic, and only with explicit opt-in.** A nested type that is not `partial` cannot carry generated member-level APIs, so the member cannot diff inside the value. Declare the nested type `partial` to enable member-level behavior, or explicitly mark the member with `[SparseMerge(MergeMode.Replace)]` to treat it as an atomic value. Otherwise the generator reports [`SPF007`](analyzer.md#spf007-unsupported-structural-member-construction) — atomic replacement is always an explicit opt-in, never a silent fallback:

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
public partial class Child { ... } // promoted nested model
```

```csharp
[SparseMerge(MergeMode.Replace)]
public Child Child { get; set; } = new(); // explicit atomic replacement
```

* **Shared nested types must agree.** A nested type without its own explicit `[SparseFragmentModel]` root that is referenced from multiple roots with different member sets or merge settings is an error ([`SPF010`](analyzer.md#spf010-incompatible-promoted-fragment-model)): unify the definitions and settings, or annotate the nested type itself with `[SparseFragmentModel]` to promote it to an explicit root.
* **Collection element types are discovered the same way.** A `List<T>` member whose `T` is a fragment model (or a reachable `partial` type) is patched element by element and needs key identity (see [Keyed collections](keyed-collections.md)); otherwise the member needs `Append`, `SetUnion`, or a custom strategy.

Element identity for element-patched collections is declared with `[SparseKey]` (single property or ordered type-level composite) or `ISparseKeyed<TKey>` — exactly one mechanism per element type. Declaration problems surface as `SPF011`–`SPF020` (see [the analyzer reference](analyzer.md#spf011-structural-sequence-without-usable-key)).

## Constructors, Members, and Cycles

* Prefer a parameterless constructor; constructor-bound properties (including `init` and `required`) resolve by name-and-type matching.
* Members marked `[JsonIgnore]` never participate in JSON conversion; members that would collide on the same JSON wire name fail during analysis ([`SPF021`](analyzer.md#spf021-duplicate-json-property-name)) rather than at runtime.
* Members named `JsonConverter` or `FragmentJsonConverter` collide with the generated JSON converter ([`SPF009`](analyzer.md#spf009-member-conflicts-with-generated-api)): rename the member.
* `From` / `Diff` reject cycles with a path-naming `NotSupportedException`, while `DeepClone` preserves them — see [Clone & ownership](cloning-and-ownership.md).
