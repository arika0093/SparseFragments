# SparseFragments Analyzer Diagnostics (SPF001–SPF032)

Diagnostics reported by the source generator `SparseFragments.Generator`.
Each diagnostic's `HelpLinkUri` points to the corresponding heading in this file.
IDs `SPF012`, `SPF015`, `SPF016`, `SPF018`, `SPF020`, and `SPF025` were retired
when keys became property-only (#206) and are not reused.

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
| [SPF013](#spf013-multiple-sparsekey-properties) | Multiple SparseKey properties | Error |
| [SPF014](#spf014-invalid-sparsekey-declaration) | Invalid SparseKey declaration | Error |
| [SPF017](#spf017-inaccessible-sparsekey-property) | Inaccessible SparseKey property | Error |
| [SPF019](#spf019-unsupported-sparsekey-shape) | Unsupported SparseKey shape | Error |
| [SPF021](#spf021-duplicate-json-property-name) | Duplicate JSON property name | Error |
| [SPF022](#spf022-sparseignore-on-key) | SparseIgnore cannot exclude a key | Error |
| [SPF023](#spf023-sparseignore-on-unsupported-property) | SparseIgnore on unsupported property | Error |
| [SPF024](#spf024-invalid-unassigned-key-sentinel) | Invalid unassigned key sentinel | Error |
| [SPF026](#spf026-in-place-submit-is-unavailable) | In-place submit is unavailable | Info |
| [SPF027](#spf027-invalid-custom-rebase-policy) | Invalid custom rebase policy | Error |
| [SPF028](#spf028-invalid-downstream-emission-plan) | Invalid downstream emission plan | Error |
| [SPF029](#spf029-unknown-product-member) | Unknown product member | Error |
| [SPF030](#spf030-invalid-comparison-strategy) | Invalid comparison strategy | Error |
| [SPF031](#spf031-invalid-sparsetemporarykey-declaration) | Invalid SparseTemporaryKey declaration | Error |
| [SPF032](#spf032-temporary-key-property-initializer) | Temporary key property has an initializer | Warning |

## SPF001: Sparse fragment model must be partial

* Message: `Model '{0}' must be declared partial`
* Cause: A type annotated with `[SparseFragmentModel]` is not declared `partial`.
  The generator appends members such as `Fragment` / `Patch` to the same type, so `partial` is required.
* Fix: Add `partial` to the type declaration.

<!-- illustrative: error illustration; the first shape does not compile by design -->
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

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
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

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
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
  `SetUnion` needs a collection, and numeric values outside `MergeMode` 0–5 are rejected.
* Fix: Select a mode that matches the member kind. See [Merge strategies](merge-strategies.md).

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
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

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
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
* Cause: A model member or directly nested type uses a name injected into the
  annotated model: `Fragment`, `FragmentBuilder`, `Patch`, `ChangeSet`,
  `ChangePayload`, `JsonConverter`, `FragmentJsonConverter`, or one of the
  generated member/method names (`Empty`, `IsEmpty`, `Merge`, `ApplyChanges`,
  `Diff`, `DeepClone`, `From`, `ToModel`, `ToBuilder`, `Build`, `ApplyTo`,
  `TryApplyTo`, `WriteTo`, `ApplyInPlace`, `ApplyInPlaceResult`).
  Names that now exist only in the per-model `SparseFragments.Generated`
  container (`Observable`, `ReadOnlyView`, `EditSession`,
  `DescriptorFactory`, operation helpers, payload DTOs, converter bodies)
  are not reserved: model members may reuse them, and collisions there are
  resolved through the central placement resolver instead.
* Fix: Rename the member.

## SPF010: Incompatible promoted fragment model

* Message: `Promoted model '{0}' requires incompatible generated semantics from different roots`
* Cause: The same nested type is reached from multiple roots whose member sets or
  merge settings disagree, so one shared generated API cannot satisfy both.
* Fix: Unify the definitions and settings, or annotate the nested type itself
  with `[SparseFragmentModel]` to make it an explicit root.

## SPF011: Structural sequence without usable key

* Message: `Member '{0}' is a structural sequence without a usable key; mark exactly one property of the element type with [SparseKey], or explicitly select MergeMode.Replace, MergeMode.Append, MergeMode.SetUnion, or a custom merge strategy`
* Cause: The generator found no stable key on the element type, so it cannot
  derive per-element patch behavior for the sequence.
* Fix: Mark exactly one property of the element type with `[SparseKey]`, or explicitly select
  `MergeMode.Replace`, `MergeMode.Append`, `MergeMode.SetUnion`, or a custom
  strategy for whole-collection semantics. The implicit default `Replace` does
  not exempt unkeyed structural sequences.
  See [Keyed collections](keyed-collections.md).

## SPF013: Multiple SparseKey properties

* Message: `Type '{0}' marks more than one property with [SparseKey]; declare exactly one [SparseKey] property per keyed element type`
* Cause: The generator found `[SparseKey]` on several properties of one
  element type. Multiple marks never form a composite key.
* Fix: Keep one `[SparseKey]` property. Composite identity uses a single
  computed property whose type holds every component (see
  [Keyed collections](keyed-collections.md#composite-identity-in-one-computed-property)).

## SPF014: Invalid SparseKey declaration

* Message: `SparseKey declaration on '{0}' is invalid; [SparseKey] targets a single property and takes no arguments`
* Cause: The declaration carries constructor arguments, or the attribute
  appears on the type itself. The attribute targets properties only.
* Fix: Use parameterless `[SparseKey]` on exactly one property per keyed
  element type.

## SPF017: Inaccessible SparseKey property

* Message: `Key property '{0}' must be a publicly readable instance property; static, indexer, or non-publicly-readable properties cannot serve as stable identity`
* Cause: The key property is static, an indexer, or not publicly readable, so
  generated code cannot reach it. Computed read-only properties are valid when public.
* Fix: Expose the key through a publicly readable instance property.

## SPF019: Unsupported SparseKey shape

* Message: `Key '{0}' has a collection-shaped type; collection-shaped keys are not supported for keyed collection identity`
* Cause: The generator found a collection-shaped key (array, `List<T>`,
  dictionary, set, …).
* Fix: Use a scalar, tuple, or value-object key type.

## SPF021: Duplicate JSON property name

* Message: `Multiple members map to the same JSON property name '{0}'`
* Cause: Two or more serialized members resolve to the same JSON wire name
  (via `[JsonPropertyName]` or the property name itself).
* Fix: Give each member a distinct explicit `[JsonPropertyName]`, or rename the
  .NET members so their wire names no longer collide.

## SPF022: SparseIgnore on key

* Message: `Property '{0}' is a SparseKey and cannot be ignored`
* Cause: The ignored property supplies stable collection identity through
  `[SparseKey]`.
* Fix: Remove `[SparseIgnore]` from the key property. Keys must remain
  available to generated collection operations. A `[SparseTemporaryKey]`
  property with `[SparseIgnore]` is likewise rejected, as an invalid
  temporary-identity declaration (see [SPF031](#spf031-invalid-sparsetemporarykey-declaration)).

## SPF023: SparseIgnore on unsupported property

* Message: `Property '{0}' cannot be ignored because required, constructor-bound without a default, or init-only properties cannot be carried through model construction`
* Cause: The generated `ApplyTo`/`TryApplyTo` APIs cannot preserve the ignored
  value while constructing a new model.
* Fix: Make the property optional and settable, or keep it in the generated
surface. Required, init-only, and non-defaulted constructor-bound properties
cannot be excluded.

<!-- illustrative: error illustration; the shape does not compile by design -->
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
* Cause: A property-level `[SparseKey(Unassigned = ...)]` value is null for a
  non-nullable key type, is not a compile-time constant, or is not compatible
  with the key property's type.
* Fix: Use a non-null sentinel constant convertible to the marked key property type.
  A null sentinel needs a nullable key type; a nullable key type without
  `Unassigned` already uses `null` as its sentinel.
  See [unassigned keys](keyed-collections.md#unassigned-keys).

## SPF026: In-place submit is unavailable

* Message: `Model '{0}' has init-only or constructor-only members and does not support in-place writes or edit-session submission`
* Cause: The model contains an init-only or get-only member. Generated `CreateEditSession()` remains available, but APIs that mutate an existing model (`Fragment.WriteTo` and `Patch.ApplyInPlace`) are omitted or unavailable.
* Fix: Make all members writable when in-place application is required. Immutable models continue to support the ordinary fragment, patch, and change-set APIs.

## SPF027: Invalid custom rebase policy

* Message: `Rebase policy for member '{0}' must derive from FragmentRebasePolicy<TMember>, be a concrete, accessible type, and target a scalar or whole-replace member`
* Cause: The generator checked the policy type against each requirement and at
  least one failed: base type or `TMember` mismatch, or the policy is `abstract`,
  generic, or not accessibly constructible. Policies also cannot target nested
  models, keyed or dictionary members, or `Append`/`SetUnion` members, which
  reconcile member by member instead of as one value.
* Fix: Derive from `FragmentRebasePolicy<TMember>` with `TMember` exactly matching
  the member type, make the policy a concrete class with an accessible
  parameterless constructor, and apply it to a scalar or whole-replace member.
  See [ChangeSet rebase](rebase.md).

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
public sealed class LabelPolicy : FragmentRebasePolicy<string?>
{
    public override bool AreEqual(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    public override bool TryRebase(Optional<string?> editBase, Optional<string?> desired, Optional<string?> current, out Optional<string?> rebased, out string? reason)
    {
        return FragmentRebasePolicy<string?>.FailOnConflict().TryRebase(editBase, desired, current, out rebased, out reason);
    }
}

[SparseFragmentModel]
public partial class PolicySettings
{
    [SparseRebasePolicy(typeof(LabelPolicy))]
    public string? Label { get; set; } = "";
}
```
## SPF028: Invalid downstream emission plan

* Message: `Invalid emission plan: {0}`
* Cause: The owning generator selected an incoherent feature set (for example a
  change set without its patch), or applied a redacted or write-only transport
  policy to a member that cannot carry one. Only scalar members accept
  non-full transports; nested members recurse through the child model's own
  member names, and keyed or dictionary members keep full disclosure.
* Fix: Enable the required feature families together, or move the transport
  policy to a scalar member.

## SPF029: Unknown product member

* Message: `Product policy references unknown member '{0}'`
* Cause: A member transport policy or write-contract mapping names a member
  that the analyzed model does not declare.
* Fix: Correct the member name in the product generator configuration so it
  matches the source member name exactly.

## SPF030: Invalid comparison strategy

* Message: `Comparison rule for member '{0}' must use a concrete accessible type implementing IEqualityComparer<TMember> with an accessible parameterless constructor`
* Cause: A `[SparseCompare]` attribute names a type that does not implement
  `IEqualityComparer<TMember>` for the member type, or the type is abstract,
  generic, or not accessibly constructible.
* Fix: Point the attribute at a concrete comparer class with an accessible
  parameterless constructor.

## SPF031: Invalid SparseTemporaryKey declaration

* Message: `Invalid temporary-identity declaration: {0}; mark exactly one Guid? property per keyed element type with parameterless [SparseTemporaryKey]`
* Cause: The `[SparseTemporaryKey]` declaration is not exactly one parameterless
  property of type `Guid?` with a public getter and setter on a keyed element
  type, it shares its property with `[SparseKey]` or `[SparseIgnore]`, or the
  element's key lacks unassigned semantics (an explicit `Unassigned` sentinel
  or a nullable key type).
* Fix: Mark one `Guid?` property with parameterless `[SparseTemporaryKey]`,
  keep it readable and settable, and give the element's `[SparseKey]` an
  unassigned state. Assign temporary values in constructors or object
  initializers instead of a property initializer (see SPF032).

## SPF032: Temporary key property initializer

* Message: `Temporary-identity property '{0}' has an explicit initializer; assign temporary values in constructors or object initializers instead`
* Cause: The `[SparseTemporaryKey]` property declares an explicit initializer.
  The check is syntactic: any `= ...` clause warns, including `= null` and
  `= default`. Assignments in constructors or object initializers are unaffected.
* Fix: Remove the initializer from the property declaration. This is a warning;
  generation continues.
