# Merge Strategies

`Merge` combines a lower-priority Fragment with a higher-priority Fragment. Missing members in the higher layer fall through to the lower layer. Most members need no configuration: scalars and ordinary collections use `Replace`, while nested generated models use `Deep`. Add `[SparseMerge]` only when you want different behavior.

## Built-in Modes

| `MergeMode` | Behavior | Applies to |
| --- | --- | --- |
| `Replace` | The higher layer's present value wins | Scalars and collections (default for both) |
| `Deep` | Recursively merge nested fragments member by member | Nested models (default for nested models) |
| `Append` | Concatenate collections from lowest to highest priority | Collections (not sets, not scalars) |
| `SetUnion` | Combine as an insertion-ordered set union | Collections and sets (not scalars) |
| `Custom` | Delegate to your own `FragmentMergeStrategy<T>` implementation | Any member via `[SparseMerge(typeof(Strategy))]` |

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

Applicability constraints (enforced at generation time, [SPF005](analyzer.md#spf005-unsupported-merge-mode)):

* `Deep` is only available for nested models (fragment models or structural types).
* `Append` cannot be used on set types (use an ordered collection or `SetUnion`) nor on non-collections.
* `SetUnion` cannot be used on non-collections.
* Out-of-range numeric mode values are rejected.

Structural sequences without a key cannot use the implicit default behavior: they must declare identity or explicitly select `Replace`, `Append`, `SetUnion`, or a custom strategy ([SPF011](analyzer.md#spf011-structural-sequence-without-usable-key)). An explicit `[SparseMerge(MergeMode.Replace)]` means the entire sequence or dictionary is replaced as one value; this also applies to keyed lists and dictionaries, and changes their ChangeSet/Patch JSON wire shape from granular entries to a whole-value operation. The implicit default `Replace` remains granular for keyed collections.

### `Append`

Present collections concatenate from lowest to highest priority: merging `["base-plugin"]` with `["extra-plugin"]` yields `["base-plugin", "extra-plugin"]`. When either side is missing, the present side wins unchanged.

### `SetUnion`

Present collections combine as an insertion-ordered set union: lower-priority entries first, then higher-priority entries not already present. Membership uses the member's equality semantics (see below).

## Custom Strategies

A custom strategy derives from `FragmentMergeStrategy<T>` and implements `Merge` and `AreEqual`. `TryRebase` is an optional override for members that need their own three-way reconciliation (see [ChangeSet rebase](rebase.md)).

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

```csharp
[SparseFragmentModel]
public partial class Policy
{
    [SparseMerge(typeof(LastWriteStrategy))]
    public string? Note { get; set; }
}
```

The contract rules:

* **Presence-aware.** `Merge` and `TryRebase` receive `Optional<T>`: `Missing` never equals a present value, including a present `null` or `default`. A `TryRebase` result that is present becomes a `Set` patch operation; a missing result becomes `Remove`; a result equal to the current state stays `Keep` (a semantic no-op).
* **Strategy validity** (enforced at generation time, `SPF004`). The strategy type must derive from `FragmentMergeStrategy<TMember>` where `TMember` exactly matches the member type; it must be a non-`abstract`, non-generic `class`; and both the type and its parameterless constructor must be `public` or `internal`. It targets a member that is not a nested model.
* **Lifetime / thread-safety.** Strategy instances are shared by generated code and may be called concurrently: keep them stateless or thread-safe.
* **Rebase default.** The default `TryRebase` succeeds when the desired state still matches the edit base (unchanged local edit — the current state wins) or when the current state matches the edit base or the desired state (clean replay or already applied), and reports a `CustomStrategy` conflict otherwise.

## Equality and Comparer Semantics

Set and dictionary members compare order-independently, and the element/key comparer is part of the collection value, so the result never depends on operand order:

* Same values with the same comparer are equal, regardless of enumeration order.
* Same values with different comparers (for example `StringComparer.Ordinal` versus `StringComparer.OrdinalIgnoreCase`) are unequal, even if the entries would match under one side's comparer.
* Reversing the operands never changes the result.
* Custom `IReadOnlyDictionary<TKey, TValue>` implementations compare order-independently even when they do not implement non-generic `ICollection`. When a custom collection does not expose its comparer, equality requires lookups to succeed in both directions using each side's own semantics.

This matters for `Diff` (whether a change is detected at all), for set-union rebase (whether a concurrent addition counts as already applied), and for custom strategies whose `AreEqual` delegates to these semantics.
