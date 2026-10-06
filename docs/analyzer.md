# SparseFragments Analyzer Diagnostics (SPF001–SPF020)

This is the list of diagnostics reported by the source generator `SparseFragments.Generator`.
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
| [SPF009](#spf009-member-conflicts-with-generated-json-patch-api) | Member conflicts with generated JSON Patch API | Error |
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
* Cause: The model is not a top-level, non-generic, non-`abstract` class or struct.
  Nested types, generic types, `abstract` types, interfaces, and other type kinds are not supported.
* Fix: Move the model to a top-level plain class/struct.
  If generics are needed, provide a materialized non-generic type instead.

## SPF003: Model needs a supported constructor

* Message: `Class model '{0}' must have a parameterless constructor or a constructor whose parameters match public readable properties by name and type; a setter, when present, must be public`
* Cause: The class model has no constructor that the generated code can call.
  It needs either a parameterless constructor or a constructor whose parameters match the public readable properties by name and type
  (a corresponding setter, when present, must be `public`).
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
* Cause: The type passed to `[SparseMerge(typeof(Strategy))]` is invalid. All of the following are required.
  * It targets a member that is not a nested model
  * It derives from `FragmentMergeStrategy<TMember>` where `TMember` exactly matches the member type
  * It is a non-`abstract`, non-generic `class`, and both the type and its parameterless constructor are `public` or `internal`
* Fix: Correct the strategy class accordingly.

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
* Cause: The combination of the `[SparseMerge]` mode and the member kind is invalid.
  * `Deep` is only available for nested models (`[SparseFragmentModel]` or structural types)
  * `Append` cannot be used on set types (use an ordered collection or `SetUnion`) nor on non-collections
  * `SetUnion` cannot be used on non-collections
  * Out-of-range numeric values (anything outside `MergeMode` 0–4) are also rejected
* Fix: Select a mode that matches the member kind.

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
* Cause: A `required` member is missing from the fragment construction plan.
  It must be readable as a public property and settable from the generated code
  (e.g. a `public` setter, or `init` covered by constructor binding).
* Fix: Declare the `required` member as a public property so that constructor binding can resolve it.
  If that is not feasible, drop `required` or reconsider the model shape.

## SPF007: Unsupported structural member construction

* Message: `Member '{0}' has an unsupported structural type; provide a supported public constructor and properties, decorate it as a fragment model, or explicitly select MergeMode.Replace`
* Cause: A nested POCO cannot participate as a sparse member.
  Non-partial nested types are treated as atomic replace values, so a nested type needs
  independently sparse behavior (a `partial` type that the generator can promote, or an
  explicit `[SparseFragmentModel]`), or the member must opt into whole-value replacement.
  Framework types and types without a supported public constructor or public properties
  are also unsupported as sparse members.
* Fix: Do one of the following.
  * Declare the nested type `partial` with a public constructor and public properties so it is promoted to a first-class fragment
  * Annotate the nested type itself with `[SparseFragmentModel]` to make it a fragment model
  * Mark the member with `[SparseMerge(MergeMode.Replace)]` to replace it as a whole

## SPF008: Unsupported deep clone member

* Message: `Member '{0}' has a reference shape that cannot be deeply cloned safely (unsupported type or constructor-bound cycle); use a supported structural type or collection, or explicitly mark a reference-safe property with SparseCloneReferenceSafe`
* Cause: The member has a reference shape that `DeepClone()` cannot copy safely.
  It contains an unsupported type or a constructor-bound reference cycle.
* Fix: Switch to a supported structural type or collection, or mark properties that may share references with `[SparseCloneReferenceSafe]`.

```csharp
[SparseFragmentModel]
public partial class Settings
{
    [SparseCloneReferenceSafe]
    public SharedService Service { get; set; } = null!;
}
```

## SPF009: Member conflicts with generated JSON Patch API

* Message: `Member '{0}' conflicts with a name reserved by the generated JSON Patch API`
* Cause: A model member is named `JsonConverter` or `FragmentJsonConverter`,
  which collides with the generated JSON Patch bridge (e.g. `Fragment.FragmentJsonConverter`).
* Fix: Rename the member.

## SPF010: Incompatible promoted fragment model

* Message: `Promoted model '{0}' requires incompatible generated semantics from different roots`
* Cause: A shared nested type without its own explicit `[SparseFragmentModel]` root is referenced
  from multiple roots with different generated semantics (different member sets or merge settings).
  The generator cannot settle on a single output, so promoted generation is skipped.
* Fix: Unify the nested type definition and merge settings, or annotate the nested type itself
  with `[SparseFragmentModel]` to promote it to an explicit root.

## SPF011: Structural sequence without usable key

* Message: `Member '{0}' is a structural sequence without a usable key; declare exactly one key on the element type (one [SparseKey] property, one type-level [SparseKey(nameof(...), ...)] composite, or one ISparseKeyed<TKey> implementation), or explicitly select MergeMode.Append, MergeMode.SetUnion, or a custom merge strategy`
* Cause: A `List<T>`/array member whose element type is a fragment model (or promotable
  partial) has no stable key, so granular add/remove/edit/order semantics cannot be derived.
  Scalar sequences (`List<string>`, `int[]`, …) and dictionaries are unaffected: scalars stay
  atomic whole values and dictionaries are keyed by `TKey` inherently.
* Fix: Declare exactly one key on the element type — one property-level `[SparseKey]`,
  one type-level `[SparseKey("TenantId", "Id")]` composite (component order is
  significant), or one `ISparseKeyed<TKey>` implementation. There is no precedence
  between mechanisms: conflicts are reported as SPF012 instead of silently picking a
  winner. Key properties must be publicly readable instance properties with non-nullable,
  non-collection types. A key change through an element edit
  is remove-old + add-new and never silently retargets. To keep legacy whole-collection
  semantics instead, select `MergeMode.Append`, `MergeMode.SetUnion`, or a custom
  `FragmentMergeStrategy<T>` on the member.

## SPF012: Conflicting SparseKey mechanisms

* Message: `Type '{0}' declares more than one SparseKey mechanism; exactly one key definition may apply (one [SparseKey] property, one type-level [SparseKey(nameof(...), ...)], or one ISparseKeyed<TKey> implementation) and there is no precedence between them`
* Cause: The type combines two or more key-definition mechanisms (e.g. a property-level
  `[SparseKey]` plus a type-level `[SparseKey(...)]`, or either plus `ISparseKeyed<TKey>`).
  Conflicting declarations are generator errors; the generator never prefers one source
  over another.
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
* Cause: More than one property carries parameterless `[SparseKey]`. Multiple
  property-level markers are never interpreted as a composite key.
* Fix: Keep a single `[SparseKey]` property, or replace the markers with one type-level
  `[SparseKey(nameof(A), nameof(B))]` composite declaration.

## SPF014: Invalid SparseKey declaration

* Message: `SparseKey declaration on '{0}' is invalid; property-level [SparseKey] takes no arguments and type-level [SparseKey] requires at least one property name`
* Cause: One of the following shapes was used.
  * Parameterless `[SparseKey]` on a type (there are no components to build a key from).
  * Property-name (or any constructor/named) arguments on a property-level `[SparseKey]`.
  * An empty type-level component list.
* Fix: Use parameterless `[SparseKey]` on exactly one property, or pass at least one
  property name to a type-level `[SparseKey("TenantId", "Id")]`.

## SPF015: Missing SparseKey component

* Message: `Key component '{0}' does not resolve to a property of the model`
* Cause: A type-level `[SparseKey(...)]` names a property that does not exist on the model
  (or only exists as a static, indexer, or non-publicly-readable member — see SPF017).
* Fix: Correct the name (component order is significant) or add the missing publicly
  readable instance property.

## SPF016: Duplicate SparseKey component

* Message: `Duplicate key component '{0}'; type-level key components must resolve to distinct properties`
* Cause: A type-level `[SparseKey(...)]` lists the same property more than once.
* Fix: List each component once, in key order.

## SPF017: Inaccessible SparseKey property

* Message: `Key property '{0}' must be a publicly readable instance property; static, indexer, or non-publicly-readable properties cannot serve as stable identity`
* Cause: A key property (property-level `[SparseKey]` or a resolved type-level component)
  is static, an indexer, or not publicly readable. Computed/read-only properties are
  valid as long as they are publicly readable instance properties
  (e.g. `public ServerKey Key => new(TenantId, Id)`).
* Fix: Expose the key through a publicly readable instance property.

## SPF018: Nullable SparseKey

* Message: `Key '{0}' must not be nullable; nullable key values/types are not supported for keyed collection identity`
* Cause: A key property, composite component, or `ISparseKeyed<TKey>` key type is nullable
  (`string?`, `int?`, …). Nullable keys cannot represent stable collection identity.
* Fix: Use a non-nullable key type.

## SPF019: Unsupported SparseKey shape

* Message: `Key '{0}' has a collection-shaped type; collection-shaped keys/components are not supported for keyed collection identity`
* Cause: A key property, composite component, or `ISparseKeyed<TKey>` key type is
  collection-shaped (arrays, `List<T>`, dictionaries, sets, …). Key equality uses the
  normal equality semantics of the key type (`EqualityComparer<T>.Default`); value-object
  keys, records, record structs, enums, strings, GUIDs and ordinary scalar types are valid
  when they provide appropriate stable equality.
* Fix: Use a scalar/value-object key type.

## SPF020: Invalid ISparseKeyed implementation

* Message: `Type '{0}' has an invalid or ambiguous ISparseKeyed<TKey> implementation; implement exactly one ISparseKeyed<TKey> with a publicly readable instance SparseKey property and a non-nullable, non-collection key type`
* Cause: The `ISparseKeyed<TKey>` escape hatch is unusable: more than one distinct `TKey`
  is implemented (ambiguous), the key type is nullable or collection-shaped, or no
  publicly readable instance `SparseKey` property is available (e.g. only an explicit
  interface implementation, which generated `element.SparseKey` extraction cannot reach).
  No separate provider SPI exists; a computed `[SparseKey]` property covers the other
  advanced cases.
* Fix: Implement exactly one `ISparseKeyed<TKey>` with an accessible `SparseKey` getter
  and a valid key type.

```csharp
// OK
public partial class Server : ISparseKeyed<ServerKey>
{
    public string Tenant { get; set; } = "";
    public int Id { get; set; }
    public ServerKey SparseKey => new(Tenant.ToUpperInvariant(), Id);
}
```
