# Merge Strategies

`Merge` combines a lower-priority Fragment with a higher-priority Fragment. Missing members in the higher layer fall through to the lower layer. Most members need no configuration: leave the member without `[SparseMerge]` and `MergeMode.Default` selects shape-aware behavior. Add `[SparseMerge]` only when you want different behavior.

## Built-in Modes

| `MergeMode` | Behavior | Applies to |
| --- | --- | --- |
| `Default` (`= 0`) | Shape-aware default: nested models merge member by member (`Deep`), everything else replaces | All members without explicit configuration |
| `Replace` | The higher layer's present value wins | Scalars and collections (type-dependent default) |
| `Deep` | Recursively merge nested fragments member by member | Nested models (type-dependent default for nested models) |
| `Append` | Concatenate collections from lowest to highest priority | Collections (not sets, not scalars) |
| `SetUnion` | Combine as an insertion-ordered set union | Collections and sets (not scalars) |
| `Custom` | Delegate to your own `FragmentMergeStrategy<T>` implementation | Any member via `[SparseMerge(typeof(Strategy))]` |

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }                    // Replace (default for scalars)

    public Child? Child { get; set; }                     // Deep (default for nested models)

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];   // concatenation

    [SparseMerge(MergeMode.SetUnion)]
    public ISet<string> Tags { get; set; } = new HashSet<string>(); // ordered union
}
```

With that shape, layering behaves member by member:

<!-- illustrative: excerpt; uses guide-local model names and does not compile as written -->
```csharp
var lower = new Settings.Fragment
{
    Label = "base",
    Child = new Child.Fragment { Host = "lower", Port = 1 },
    Plugins = new[] { "base-plugin" },
};
var higher = new Settings.Fragment
{
    Child = new Child.Fragment { Host = "higher" },
    Plugins = new[] { "extra-plugin" },
};

var merged = lower.Merge(higher);
// merged.Label == "base" (higher is missing, so the lower value falls through)
// merged.Child.Host == "higher", merged.Child.Port == 1 (Deep composes member by member)
// merged.Plugins == ["base-plugin", "extra-plugin"] (Append concatenates)
```

Applicability constraints (enforced at generation time, [SPF005](analyzer.md#spf005-unsupported-merge-mode)):

* `Deep` is only available for nested models (fragment models or structural types).
* `Append` cannot be used on set types (use an ordered collection or `SetUnion`) nor on non-collections.
* `SetUnion` cannot be used on non-collections.
* Out-of-range numeric mode values are rejected.

Structural sequences without a key cannot use the implicit default behavior: they must declare identity or explicitly select `Replace`, `Append`, `SetUnion`, or a custom strategy ([SPF011](analyzer.md#spf011-structural-sequence-without-usable-key)).

An explicit `[SparseMerge(MergeMode.Replace)]` means the entire sequence or dictionary is replaced as one value; this also applies to keyed lists and dictionaries, and changes their ChangeSet payload JSON from granular entries to a whole-value operation. The implicit `Default` remains granular for keyed collections: it resolves to `Deep` for nested models and to `Replace` for everything else.

Merge modes and Patch/ChangeSet granularity answer different questions. A merge mode decides how two fragment layers combine when both are present. Patch and ChangeSet granularity decides how an edit is expressed and sent: per-key operations for keyed lists and dictionaries under the implicit default, or one whole-value replacement under explicit `Replace`. Choosing `Replace` on a keyed member keeps merge behavior simple but gives up per-item add/remove/edit tracking for that member; see [Keyed collections](keyed-collections.md#atomic-vs-keyed-collections).

### `Append`

Present collections concatenate from lowest to highest priority: merging `["base-plugin"]` with `["extra-plugin"]` yields `["base-plugin", "extra-plugin"]`. When either side is missing, the present side wins unchanged.

### `SetUnion`

Present collections combine as an insertion-ordered set union: lower-priority entries first, then higher-priority entries not already present. Membership uses the member's equality semantics (see below).

## Custom Strategies

A custom strategy derives from `FragmentMergeStrategy<T>` and implements `Merge` and `AreEqual`. `TryRebase` is an optional override for members that need their own three-way reconciliation (see [ChangeSet rebase](rebase.md)).

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
public sealed class LastWriteStrategy : FragmentMergeStrategy<string?>
{
    public override Optional<string?> Merge(
        Optional<string?> lowerPriority,
        Optional<string?> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(string? left, string? right) => left == right;
}
```

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
[SparseFragmentModel]
public partial class Policy
{
    [SparseMerge(typeof(LastWriteStrategy))]
    public string? Note { get; set; }
}
```

The contract rules:

* `Presence-aware.` `Merge` and `TryRebase` receive `Optional<T>`: `Missing` never equals a present value, including a present `null` or `default`. A `TryRebase` result that is present becomes a `Set` patch operation; a missing result becomes `Remove`; a result equal to the current state stays `Keep` (a semantic no-op).
* `Strategy validity` (enforced at generation time, `SPF004`). The strategy type must derive from `FragmentMergeStrategy<TMember>` where `TMember` exactly matches the member type; it must be a non-`abstract`, non-generic `class`; and both the type and its parameterless constructor must be `public` or `internal`. It targets a member that is not a nested model.
* `Lifetime and thread-safety.` Strategy instances are shared by generated code and may be called concurrently: keep them stateless or thread-safe.
* `Rebase default.` The default `TryRebase` succeeds when the desired state still matches the edit base (unchanged local edit, so the current state wins) or when the current state matches the edit base or the desired state (clean replay or already applied), and reports a `CustomStrategy` conflict otherwise.

A member can carry both a merge strategy and a rebase policy. The strategy keeps owning `Merge`; the policy takes precedence for that member during rebase (see [ChangeSet rebase](rebase.md#rebase-policies)). A policy without any strategy needs no merge configuration at all.

## Inspecting Value Origins

A fragment can carry an optional origin label for inspection, for example to show which configuration layer supplied each effective value. Pass it at construction; the parameterless constructor keeps working and means Unknown:

<!-- illustrative: excerpt; uses guide-local model names and does not compile as written -->
```csharp
var defaults = new Settings.Fragment("defaults") { Label = "base" };
var tenant = new Settings.Fragment("tenant") { Label = "custom" };

var effective = defaults.Merge(tenant);
var labelOrigin = effective.GetOrigin(Settings.SparsePath.Label); // "tenant"
```

`Merge` propagates attribution with the same rules as values. Replacement members take the winning side, deep merges attribute each leaf, `Append` tracks element ranges, and `SetUnion` attributes each surviving element to its first contributor under the member comparer. A present `null` counts as an attributed reset from the side that set it. Members with a custom strategy report Unknown. Nested fragments inherit the enclosing default unless they name their own origin, which then wins for their subtree.

Read the effective state with `GetOrigin` (null for Unknown or absent paths), `TryGetOrigin` (separate the two cases), `EnumerateOrigins` (one entry per attributable value, with element entries such as `Plugins[0]` for tracked collections), `GetByOrigin` (read-only projection for one origin), and `SplitByOrigin` (per-origin projections in first-seen order). Member-level reads over mixed collections or deep-merged children report Unknown; read their elements or leaves for precise attribution.

Grouping is lossy by design. Overwritten lower-priority values survive in no group, and regrouping never reconstructs the original layers. Origins stay out of the semantic state: equality, `Diff`, `ChangeSet` creation, Patch operations, and the wire format ignore them. Applying a `Patch` resets attribution to Unknown because patch operations carry no origin metadata.

Paths are the canonical `SparsePath` values: use the typed per-model navigation (`Settings.SparsePath.Theme`, `Settings.SparsePath.Nested.Host`) or parse the wire text (`SparsePath.Parse<Settings>("Child.Host")`, `SparsePath.Parse<Settings>("Tags[0]")`). The query signatures take `SparsePath`, so typed paths bind without further changes.

## Semantic Equality with [SparseCompare]

`MergeMode` decides how present values combine; `[SparseCompare]` decides when two values count as equal. Place it on a fragment model or once per assembly:

<!-- sample: merge-compare-models -->
```csharp
public readonly struct MergeToken
{
    public MergeToken(string value) => Value = value;

    public string Value { get; }
}

public sealed class MergeTokenComparer : IEqualityComparer<MergeToken>
{
    public bool Equals(MergeToken left, MergeToken right) =>
        string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(MergeToken value) =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value);
}

[SparseCompare(typeof(MergeToken), typeof(MergeTokenComparer))]
[SparseFragmentModel]
public partial class MergeDocsCompareSettings
{
    public MergeToken Token { get; set; }
}
```
<!-- /sample -->

The rules, verified against the generator analysis:

* `Exact member type.` The first argument must be the exact member type, not a base type or an interface.
* `Comparer shape.` The comparer implements `IEqualityComparer<T>` for that exact `T` and exposes an accessible parameterless constructor.
* `Scope and inheritance.` A model-level rule applies to that model. An assembly-level rule applies compilation-wide. A nested model referenced by annotated roots inherits the rule when every referencing root agrees on one comparer for the type; one silent root or one conflicting comparer vetoes the inheritance.
* `Lifetime.` Generated code keeps one comparer instance per configured member and may call it concurrently, so implementations stay stateless or thread-safe.
* `Effect.` The comparer governs `Diff` detection, `ChangeSet.Between` matching, `Compose` contiguity checks, and rebase equality for that type. Collection membership still uses the collection's own comparer: a `HashSet<string>` built with `StringComparer.OrdinalIgnoreCase` compares case-insensitively regardless of `[SparseCompare]`.
* `Against custom AreEqual.` A member with its own `[SparseMerge(typeof(...))]` strategy keeps its own `AreEqual` and does not use the type comparer. `[SparseCompare]` supplies the type-level default everywhere else.

Comparer-equal values report no change:

<!-- sample: merge-compare -->
```csharp
var before = new MergeDocsCompareSettings { Token = new MergeToken("a") };
var after = new MergeDocsCompareSettings { Token = new MergeToken("A") };

var diff = MergeDocsCompareSettings.Fragment.Diff(before, after);
// diff.Token.IsPresent == false (comparer-equal: no change detected)
```
<!-- /sample -->

## Equality and Comparer Semantics

Set and dictionary members compare order-independently, and the element/key comparer is part of the collection value, so the result never depends on operand order:

* Same values with the same comparer are equal, regardless of enumeration order.
* Same values with different comparers (for example `StringComparer.Ordinal` versus `StringComparer.OrdinalIgnoreCase`) are unequal, even if the entries would match under one side's comparer.
* Reversing the operands never changes the result.
* Custom `IReadOnlyDictionary<TKey, TValue>` implementations compare order-independently even when they do not implement non-generic `ICollection`. When a custom collection does not expose its comparer, equality requires lookups to succeed in both directions using each side's own semantics.

These semantics decide `Diff` results (whether a change is detected at all), set-union rebase (whether a concurrent addition counts as already applied), and custom strategies whose `AreEqual` delegates to these semantics.
