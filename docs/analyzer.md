# SparseFragments Analyzer Diagnostics (SPF001–SPF026)

Diagnostics reported by the source generator `SparseFragments.Generator`.
Each diagnostic's `HelpLinkUri` points to the corresponding heading in this file.

| ID | Title | Severity |
| --- | --- | --- |
| [SPF001](#spf001-sparse-fragment-model-must-be-partial) | Sparse fragment model must be partial | Error |
| [SPF002](#spf002-unsupported-sparse-fragment-model) | Unsupported sparse fragment model | Error |
| [SPF003](#spf003-model-needs-a-supported-constructor) | Model needs a supported constructor | Error |
| [SPF004](#spf004-invalid-custom-merge-strategy) | Invalid custom merge strategy | Error |
| [SPF005](#spf005-unsupported-merge-mode) | Unsupported merge mode | Error |
| [SPF006](#spf006-required-member-cannot-be-constructed) | Required member cannot be constructed | Error |
| [SPF007](#spf007-unsupported-structural-member-construction) | Unsupported structural member construction | Error |
| [SPF008](#spf008-unsupported-deep-clone-member) | Unsupported deep clone member | Error |
| [SPF009](#spf009-member-conflicts-with-generated-api) | Member conflicts with generated API | Error |
| [SPF010](#spf010-incompatible-promoted-fragment-model) | Incompatible promoted fragment model | Error |
| [SPF011](#spf011-structural-sequence-without-usable-key) | Structural sequence without usable key | Error |
| [SPF012](#spf012-conflicting-sparsekey-mechanisms) | Conflicting SparseKey mechanisms | Error |
| [SPF013](#spf013-multiple-sparsekey-properties) | Multiple SparseKey properties | Error |
| [SPF014](#spf014-invalid-sparsekey-declaration) | Invalid SparseKey declaration | Error |
| [SPF015](#spf015-missing-sparsekey-component) | Missing SparseKey component | Error |
| [SPF016](#spf016-duplicate-sparsekey-component) | Duplicate SparseKey component | Error |
| [SPF017](#spf017-inaccessible-sparsekey-property) | Inaccessible SparseKey property | Error |
| [SPF018](#spf018-nullable-sparsekey) | Nullable SparseKey | Error |
| [SPF019](#spf019-unsupported-sparsekey-shape) | Unsupported SparseKey shape | Error |
| [SPF020](#spf020-invalid-isparsekeyed-implementation) | Invalid ISparseKeyed implementation | Error |
| [SPF021](#spf021-duplicate-json-property-name) | Duplicate JSON property name | Error |
| [SPF022](#spf022-sparseignore-on-key) | SparseIgnore on key | Error |
| [SPF023](#spf023-sparseignore-on-unsupported-property) | SparseIgnore on unsupported property | Error |
| [SPF024](#spf024-invalid-unassigned-key-sentinel) | Invalid unassigned key sentinel | Error |
| [SPF025](#spf025-unsupported-unassigned-key-sentinel) | Unsupported unassigned key sentinel | Error |
| [SPF026](#spf026-in-place-submit-is-unavailable) | In-place submit is unavailable | Info |

## SPF001: Sparse fragment model must be partial

* Message: `Model '{0}' must be declared partial`
* Cause: A type annotated with `[SparseFragmentModel]` is not declared `partial`.
  The generator appends members such as `Fragment` / `Patch` to the same type, so `partial` is required.
* Fix: Add `partial` to the type declaration.

```csharp
// Does not compile
[SparseFragmentModel]
public class Settings { ... }

// OK
[SparseFragmentModel]
public partial class Settings { ... }
```

## SPF002: Unsupported sparse fragment model

* Message: `Model '{0}' must be a top-level, non-generic, non-abstract class or struct`
* Cause: The model is not a top-level, non-generic, non-`abstract` class or struct
  (`ref` structs and `file`-local types are likewise unsupported).
* Fix: Move the model to a top-level plain class/struct.
  If generics are needed, provide a materialized non-generic type instead.

## SPF003: Model needs a supported constructor

* Message: `Class model '{0}' must have a parameterless constructor or a constructor whose parameters match public readable properties by name and type; a setter, when present, must be public`
* Cause: The generator found no constructor it can call: neither a parameterless
  constructor nor one whose parameters match the public readable properties by name and type.
* Fix: Add a parameterless constructor, or provide a constructor that corresponds to the properties.

```csharp
[SparseFragmentModel]
public partial class Settings
{
    public Settings() { }
    public string? Label { get; set; }
}
```

## SPF004: Invalid custom merge strategy

* Message: `Merge strategy for member '{0}' must derive from FragmentMergeStrategy<TMember> and be a concrete, accessible type`
* Cause: The generator checked the strategy type against each requirement and at
  least one failed: member is a nested model, base type or `TMember` mismatch,
  or the strategy is `abstract`, generic, or not accessibly constructible.
* Fix: Derive from `FragmentMergeStrategy<TMember>` with `TMember` exactly matching
  the member type, and make the strategy a concrete class with an accessible
  parameterless constructor. See [Merge strategies](merge-strategies.md).

```csharp
public sealed class SumMergeStrategy : FragmentMergeStrategy<List<int>>
{
    public SumMergeStrategy() { }
    public override Optional<List<int>> Merge(Optional<List<int>> lower, Optional<List<int>> higher) => higher.IsPresent ? higher : lower;
    public override bool AreEqual(List<int>? left, List<int>? right) => left == right;
}

[SparseFragmentModel]
public partial class StrategySettings
{
    [SparseMerge(typeof(SumMergeStrategy))]
    public List<int> Values { get; set; } = [];
}
```

## SPF005: Unsupported merge mode

* Message: `The configured merge mode is not supported for member '{0}'`
* Cause: The mode does not apply to this member kind:
  `Deep` needs a nested generated model, `Append` needs an ordered collection,
  `SetUnion` needs a collection, and numeric values outside `MergeMode` 0–4 are rejected.
* Fix: Select a mode that matches the member kind. See [Merge strategies](merge-strategies.md).

```csharp
[SparseFragmentModel]
public partial class Settings
{
    [SparseMerge(MergeMode.Append)] // OK for IReadOnlyList<string>, not for ISet<string>
    public IReadOnlyList<string> Plugins { get; set; } = [];

    [SparseMerge(MergeMode.SetUnion)] // For set types
    public ISet<string> Tags { get; set; } = new HashSet<string>();
}
```

## SPF006: Required member cannot be constructed

* Message: `Required member '{0}' must be represented by an accessible public property in the fragment construction plan`
* Cause: The generator found no way to assign the member: no `public` setter
  and no constructor parameter matching it.
* Fix: Give the member a `public` setter or a matching constructor parameter;
  otherwise drop `required`. See [Model shapes](model-shapes.md).

## SPF007: Unsupported structural member construction

* Message: `Member '{0}' has an unsupported structural type; provide a supported public constructor and properties, decorate it as a fragment model, or explicitly select MergeMode.Replace`
* Cause: The generator patches inside a nested value only through generated
  member-level APIs, which need the nested type to be `partial` (or an explicit
  fragment model). The member also lacks an explicit `Replace` opt-in, so the
  generator refuses to guess.
* Fix: Declare the nested type `partial`, annotate it with `[SparseFragmentModel]`,
  or mark the member `[SparseMerge(MergeMode.Replace)]`. See [Model shapes](model-shapes.md).

## SPF008: Unsupported deep clone member

* Message: `Member '{0}' has a reference shape that cannot be deeply cloned safely (unsupported type or constructor-bound cycle); use a supported structural type or collection, or explicitly mark a reference-safe property with SparseCloneReferenceSafe`
* Cause: `DeepClone` must copy the member's object graph, but the member's type
  is outside the supported cloneable shapes or closes a constructor-bound cycle
  the cloner cannot rebuild.
* Fix: Switch to a supported structural type or collection, or mark the property
  `[SparseCloneReferenceSafe]`. See [Cloning and ownership](cloning-and-ownership.md).

```csharp
[SparseFragmentModel]
public partial class Settings
{
    [SparseCloneReferenceSafe]
    public SharedService Service { get; set; } = null!;
}
```

## SPF009: Member conflicts with generated API

* Message: `Member '{0}' conflicts with a name reserved by the generated API`
* Cause: A model member is named `JsonConverter` or `FragmentJsonConverter`,
  which collides with the generated fragment JSON converter.
* Fix: Rename the member.

## SPF010: Incompatible promoted fragment model

* Message: `Promoted model '{0}' requires incompatible generated semantics from different roots`
* Cause: The same nested type is reached from multiple roots whose member sets or
  merge settings disagree, so one shared generated API cannot satisfy both.
* Fix: Unify the definitions and settings, or annotate the nested type itself
  with `[SparseFragmentModel]` to make it an explicit root.

## SPF011: Structural sequence without usable key

* Message: `Member '{0}' is a structural sequence without a usable key; declare exactly one key on the element type (one [SparseKey] property, one type-level [SparseKey(nameof(...), ...)] composite, or one ISparseKeyed<TKey> implementation), or explicitly select MergeMode.Replace, MergeMode.Append, MergeMode.SetUnion, or a custom merge strategy`
* Cause: The generator found no stable key on the element type, so it cannot
  derive per-element patch behavior for the sequence.
* Fix: Declare exactly one key on the element type, or explicitly select
  `MergeMode.Replace`, `MergeMode.Append`, `MergeMode.SetUnion`, or a custom
  strategy for whole-collection semantics. The implicit default `Replace` does
  not exempt unkeyed structural sequences.
  See [Keyed collections](keyed-collections.md).

## SPF012: Conflicting SparseKey mechanisms

* Message: `Type '{0}' declares more than one SparseKey mechanism; exactly one key definition may apply (one [SparseKey] property, one type-level [SparseKey(nameof(...), ...)], or one ISparseKeyed<TKey> implementation) and there is no precedence between them`
* Cause: The generator observed two or more key-definition mechanisms on the type
  and never prefers one over another.
* Fix: Keep exactly one mechanism and remove the others.

```csharp
// Does not compile: property-level key + type-level composite conflict (SPF012)
[SparseKey(nameof(TenantId), nameof(Id))]
public partial class Server
{
    [SparseKey]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
}

// OK: one mechanism
[SparseKey(nameof(TenantId), nameof(Id))]
public partial class Server
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; }
}
```

## SPF013: Multiple SparseKey properties

* Message: `Type '{0}' marks more than one property with [SparseKey]; multiple property-level keys are not a composite key, use a single type-level [SparseKey(nameof(...), ...)] declaration instead`
* Cause: The generator found parameterless `[SparseKey]` on several properties,
  which it never reads as a composite key.
* Fix: Keep one `[SparseKey]` property or use a single type-level composite.

## SPF014: Invalid SparseKey declaration

* Message: `SparseKey declaration on '{0}' is invalid; property-level [SparseKey] takes no arguments and type-level [SparseKey] requires at least one property name`
* Cause: The declaration matches none of the supported shapes: parameterless on a
  type, arguments on a property-level marker, or an empty type-level component list.
* Fix: Use parameterless `[SparseKey]` on one property, or pass at least one
  property name to a type-level `[SparseKey("TenantId", "Id")]`.

## SPF015: Missing SparseKey component

* Message: `Key component '{0}' does not resolve to a property of the model`
* Cause: The named component resolves to no usable instance property on the model.
* Fix: Correct the name (component order is significant) or add the missing property.

## SPF016: Duplicate SparseKey component

* Message: `Duplicate key component '{0}'; type-level key components must resolve to distinct properties`
* Cause: The same property appears twice in one type-level component list.
* Fix: List each component once, in key order.

## SPF017: Inaccessible SparseKey property

* Message: `Key property '{0}' must be a publicly readable instance property; static, indexer, or non-publicly-readable properties cannot serve as stable identity`
* Cause: The key property is static, an indexer, or not publicly readable, so
  generated code cannot reach it. Computed read-only properties are valid when public.
* Fix: Expose the key through a publicly readable instance property.

## SPF018: Nullable SparseKey

* Message: `Key '{0}' must not be nullable; nullable key values/types are not supported for keyed collection identity`
* Cause: The generator found a nullable key (`string?`, `int?`, …), which cannot
  serve as stable collection identity.
* Fix: Use a non-nullable key type.

## SPF019: Unsupported SparseKey shape

* Message: `Key '{0}' has a collection-shaped type; collection-shaped keys/components are not supported for keyed collection identity`
* Cause: The generator found a collection-shaped key (array, `List<T>`,
  dictionary, set, …).
* Fix: Use a scalar/value-object key type.

## SPF020: Invalid ISparseKeyed implementation

* Message: `Type '{0}' has an invalid or ambiguous ISparseKeyed<TKey> implementation; implement exactly one ISparseKeyed<TKey> with a publicly readable instance SparseKey property and a non-nullable, non-collection key type`
* Cause: The generator cannot use the implementation: ambiguous `TKey`s, a
  nullable or collection-shaped key type, or no reachable instance `SparseKey`
  getter (explicit interface implementations are invisible to generated code).
* Fix: Implement exactly one `ISparseKeyed<TKey>` with an accessible `SparseKey`
  getter and a valid key type. See [Keyed collections](keyed-collections.md).

```csharp
// OK
public partial class Server : ISparseKeyed<ServerKey>
{
    public string Tenant { get; set; } = "";
    public int Id { get; set; }
    public ServerKey SparseKey => new(Tenant.ToUpperInvariant(), Id);
}
```

## SPF021: Duplicate JSON property name

* Message: `Multiple members map to the same JSON property name '{0}'`
* Cause: Two or more serialized members resolve to the same JSON wire name
  (via `[JsonPropertyName]` or the property name itself).
* Fix: Give each member a distinct explicit `[JsonPropertyName]`, or rename the
  .NET members so their wire names no longer collide.

## SPF022: SparseIgnore on key

* Message: `Property '{0}' is a SparseKey or a component of a composite SparseKey and cannot be ignored`
* Cause: The ignored property supplies stable collection identity, either through
  `[SparseKey]`, a type-level composite declaration, or `ISparseKeyed<TKey>`.
* Fix: Remove `[SparseIgnore]` from the key property/component. Keys must remain
  available to generated collection operations.

## SPF023: SparseIgnore on unsupported property

* Message: `Property '{0}' cannot be ignored because required, constructor-bound without a default, or init-only properties cannot be carried through model construction`
* Cause: The generated `ApplyTo`/`TryApplyTo` APIs cannot preserve the ignored
  value while constructing a new model.
* Fix: Make the property optional and settable, or keep it in the generated
  surface. Required, init-only, and non-defaulted constructor-bound properties
  cannot be excluded.

```csharp
// Does not compile: both members serialize as "dup"
[SparseFragmentModel]
public partial class Widget
{
    [JsonPropertyName("dup")]
    public string? First { get; set; }
    [JsonPropertyName("dup")]
    public string? Second { get; set; }
}
```

## SPF024: Invalid unassigned key sentinel

* Message: `Invalid unassigned key sentinel: {0}`
* Cause: A property-level `[SparseKey(Unassigned = ...)]` value is null, is not a
  compile-time constant, or is not compatible with the key property's type.
* Fix: Use a non-null sentinel constant convertible to the marked key property type.
  Unassigned sentinels are opt-in and are supported only for property-level keys.
  See [database-assigned keys](keyed-collections.md#database-assigned-keys).

## SPF025: Unsupported unassigned key sentinel

* Message: `Unassigned key sentinels are not supported for composite or interface keys on '{0}'`
* Cause: `Unassigned` was specified on a type-level composite key declaration.
  `ISparseKeyed<TKey>` likewise does not support unassigned sentinels.
* Fix: Use one property-level `[SparseKey(Unassigned = ...)]` key, or omit the
  sentinel and retain the existing unique-key requirement.

## SPF026: In-place submit is unavailable

* Message: `Model '{0}' has init-only or constructor-only members and does not support in-place writes or edit-session submission`
* Cause: The model contains an init-only or get-only member. Generated `CreateEditSession()` remains available, but APIs that mutate an existing model (`Fragment.WriteTo`, `Patch.ApplyInPlace`, and session submit) are omitted or unavailable.
* Fix: Make all members writable when an in-place submit workflow is required. Immutable models continue to support the ordinary fragment, patch, and change-set APIs.
