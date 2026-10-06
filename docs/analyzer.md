# SparseFragments Analyzer Diagnostics (SPF001–SPF010)

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
