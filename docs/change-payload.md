# ChangePayload wire reference

A `ChangePayload` is the versioned JSON envelope that carries a `ChangeSet` transition, a `Patch` command, or a mix of both across a process boundary. It names members and carries typed values, so each side converts the envelope into its own `Patch` or `ChangeSet`. The library performs no domain-specific model mapping, and the client and server models may differ.

The current format token is `"version": "0.1"`. Treat it as the documented current format, not a promise of indefinite backwards compatibility. For the in-memory transition and command types, see [Fragments and patches](fragments-and-patches.md). For reconciling a received transition with current state, see [ChangeSet rebase](rebase.md).

<!-- sample: payload-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class WireSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}

[SparseFragmentModel]
public partial class WireOrder
{
    public string? Name { get; set; }

    public WireCustomer? Customer { get; set; }
}

public partial class WireCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class WireFleet
{
    public List<WireServer> Servers { get; set; } = new();
}

public partial class WireServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class WireSecret
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
}
```
<!-- /sample -->

Serialize and deserialize the generated `T.ChangePayload`, not `T.ChangeSet` or `T.Patch`. The typed member variants are suitable for OpenAPI endpoint schemas.

## Envelope layout

The top-level object carries the format token and the member changes. Nested changes omit the token. The smallest valid envelope carries one member transition with both endpoints present:

<!-- json-specimen: payload-envelope -->
```json
{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"}}]}
```
<!-- /json-specimen -->

| Field | Meaning |
| --- | --- |
| `version` | Required format token, currently `"0.1"`. |
| `changes` | Member changes in deterministic order. |
| `member` | CLR member name, or `"$root"` for whole-contribution transitions. |
| `before` / `after` | Presence-aware endpoints for scalar and nested members. |
| `nested` | Nested `changes` array for a nested model member. |
| `items` | Keyed or dictionary item operations for collection members. |
| `beforeOrder` / `afterOrder` | Key sequences for keyed members. |

Payload DTO property names use camel case, `kind` values are lowercase, and property order is explicit. Embedded model values follow the application JSON metadata. Unused item fields stay omitted: an edit item carries only its nested `edit` payload, an addition carries only the endpoint needed to apply it, and a removal carries only the endpoint needed to apply it. Value-free endpoints still serialize an explicit `"value": null`, as the examples below show.

## Endpoint states

Each endpoint carries a `state`: `missing` (known absent), `null` (explicit null), `value` (with a `value`), or `redacted` (deliberately undisclosed).

| State | JSON | Meaning |
| --- | --- | --- |
| `missing` | `{"state":"missing","value":null}` | Known absent. An addition arrives with a missing before-state; a removal leaves a missing after-state. |
| `null` | `{"state":"null","value":null}` | Explicit null. Present, and the value is null. |
| `value` | `{"state":"value","value":"after"}` | Present with a value. |
| `redacted` | `{"state":"redacted","value":null}` | Undisclosed before-state. Never read as missing. |

Redacted is never read as missing. A redacted before-state must arrive with an explicit desired after-state; envelopes that omit it, or that redact an after-state, are rejected.

## Scalar set

<!-- sample: payload-scalar-set -->
```csharp
var setBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var setAfter = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
);

var setPayload = WireSettings.ChangeSet.Between(setBefore, setAfter).ToPayload();
var setJson = JsonSerializer.Serialize(setPayload);
// setJson == {"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"}}]}
var setRestored = JsonSerializer
    .Deserialize<WireSettings.ChangePayload>(setJson)!
    .ToChangeSet();
// setRestored.ToPatch().Apply(setBefore) replays setAfter
```
<!-- /sample -->

The JSON above is actual serializer output. Only the changed member travels. An addition mirrors this shape with a missing before-state:

<!-- json-specimen: payload-scalar-add -->
```json
{"version":"0.1","changes":[{"member":"Label","before":{"state":"missing","value":null},"after":{"state":"value","value":"after"}}]}
```
<!-- /json-specimen -->

## Explicit null

An explicit null stays present. It is distinct from both a value change and a removal.

<!-- sample: payload-explicit-null -->
```csharp
var nullBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var nullAfter = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = null, RetryCount = 1 })
);

var nullPayload = WireSettings.ChangeSet.Between(nullBefore, nullAfter).ToPayload();
var nullJson = JsonSerializer.Serialize(nullPayload);
// nullJson == {"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"null","value":null}}]}
var nullRestored = JsonSerializer
    .Deserialize<WireSettings.ChangePayload>(nullJson)!
    .ToChangeSet();
// nullRestored.ToPatch().Apply(nullBefore) replays nullAfter
```
<!-- /sample -->

## Removal

A removal leaves a missing after-state. Missing never equals a present value, not even a present null or default, so the transition stays observable.

<!-- sample: payload-remove -->
```csharp
var removeBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var removeAfter = Optional<WireSettings.Fragment?>.Present(
    new WireSettings.Fragment { RetryCount = 1 }
);

var removePayload = WireSettings.ChangeSet.Between(removeBefore, removeAfter).ToPayload();
var removeJson = JsonSerializer.Serialize(removePayload);
// removeJson == {"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"missing","value":null}}]}
var removeRestored = JsonSerializer
    .Deserialize<WireSettings.ChangePayload>(removeJson)!
    .ToChangeSet();
// !removeRestored.ToPatch().Apply(removeBefore).Value!.Label.IsPresent
```
<!-- /sample -->

## Nested changes

A nested member recurses through the same typed shape under its `nested` payload.

<!-- sample: payload-nested -->
```csharp
var nestedBefore = Optional<WireOrder.Fragment?>.Present(
    WireOrder.Fragment.From(
        new WireOrder { Name = "a", Customer = new WireCustomer { Name = "Ann" } }
    )
);
var nestedAfter = Optional<WireOrder.Fragment?>.Present(
    WireOrder.Fragment.From(
        new WireOrder { Name = "a", Customer = new WireCustomer { Name = "Bob" } }
    )
);

var nestedPayload = WireOrder.ChangeSet.Between(nestedBefore, nestedAfter).ToPayload();
var nestedJson = JsonSerializer.Serialize(nestedPayload);
// nestedJson carries member "Customer" with a nested changes array and no inner version
var nestedRestored = JsonSerializer
    .Deserialize<WireOrder.ChangePayload>(nestedJson)!
    .ToChangeSet();
// nestedRestored.Customer.Name.After.Value == "Bob"
```
<!-- /sample -->

<!-- json-specimen: payload-nested-json -->
```json
{"version":"0.1","changes":[{"member":"Customer","nested":{"changes":[{"member":"Name","before":{"state":"value","value":"Ann"},"after":{"state":"value","value":"Bob"}}]}}]}
```
<!-- /json-specimen -->

## Keyed changes

Keyed members rebase element-wise where keys line up. Each item names its `key`, its `kind`, and its positions. `beforeIndex` and `afterIndex` are absolute zero-based positions in the before and after order; an addition carries `beforeIndex` -1, a removal carries `afterIndex` -1. The example below moves `["a","b"]` to `["b","c"]`, so the added key `c` lands at `afterIndex` 1.

<!-- sample: payload-keyed -->
```csharp
var keyedBefore = Optional<WireFleet.Fragment?>.Present(
    WireFleet.Fragment.From(
        new WireFleet
        {
            Servers = new()
            {
                new WireServer { Id = "a", Host = "A" },
                new WireServer { Id = "b", Host = "B" },
            },
        }
    )
);
var keyedAfter = Optional<WireFleet.Fragment?>.Present(
    WireFleet.Fragment.From(
        new WireFleet
        {
            Servers = new()
            {
                new WireServer { Id = "b", Host = "B2" },
                new WireServer { Id = "c", Host = "C" },
            },
        }
    )
);

var keyedPayload = WireFleet.ChangeSet.Between(keyedBefore, keyedAfter).ToPayload();
var keyedJson = JsonSerializer.Serialize(keyedPayload);
// keyedJson carries one edit item, one add item, one remove item,
// plus beforeOrder ["a","b"] and afterOrder ["b","c"]
var keyedRestored = JsonSerializer
    .Deserialize<WireFleet.ChangePayload>(keyedJson)!
    .ToChangeSet();
// keyedRestored.Servers.GetChange("b").IsEdited == true
// keyedRestored.Servers.GetChange("c").IsAdded == true
// keyedRestored.Servers.GetChange("a").IsRemoved == true
```
<!-- /sample -->

<!-- json-specimen: payload-keyed-json -->
```json
{"version":"0.1","changes":[{"member":"Servers","items":[{"key":"b","kind":"edit","beforeIndex":1,"afterIndex":0,"edit":{"changes":[{"member":"Host","before":{"state":"value","value":"B"},"after":{"state":"value","value":"B2"}}]}},{"key":"c","kind":"add","beforeIndex":-1,"afterIndex":1,"after":{"state":"value","value":{"Id":"c","Host":"C"}}},{"key":"a","kind":"remove","beforeIndex":0,"afterIndex":-1,"before":{"state":"value","value":{"Id":"a","Host":"A"}}}],"beforeOrder":["a","b"],"afterOrder":["b","c"]}]}
```
<!-- /json-specimen -->

A pure reorder carries `reorder` items with indexes and the before and after key sequences:

<!-- json-specimen: payload-reorder -->
```json
{"version":"0.1","changes":[{"member":"Servers","items":[{"key":"b","kind":"reorder","beforeIndex":1,"afterIndex":0,"isReordered":true},{"key":"a","kind":"reorder","beforeIndex":0,"afterIndex":1,"isReordered":true}],"beforeOrder":["a","b"],"afterOrder":["b","a"]}]}
```
<!-- /json-specimen -->

Scalar dictionary members use the same item vocabulary without positions. The example below edits key `b`, removes key `a`, and adds key `c` on a `Dictionary<string, int>` member:

<!-- json-specimen: payload-dict -->
```json
{"version":"0.1","changes":[{"member":"Scores","items":[{"key":"c","kind":"add","after":{"state":"value","value":4}},{"key":"a","kind":"remove","before":{"state":"value","value":1}},{"key":"b","kind":"edit","before":{"state":"value","value":2},"after":{"state":"value","value":3}}]}]}
```
<!-- /json-specimen -->

Per-item redacted endpoints in keyed and dictionary members are rejected with a typed error. Send a whole-member blind set for those members. Blind whole-collection removal has no patch projection.

Additions of unassigned elements with `[SparseTemporaryKey]` travel as keyed
add items carrying the temporary Guid, so each pending addition keeps its own
identity through serialization instead of sharing the sentinel plus a position.
`ToChangeSet()` restores the additions under their Guids, and later edits,
removals, and reorders address them the same way. The conversion round-trip is
shown in [Keyed collections](keyed-collections.md#temporary-identity-for-pending-additions).

## Whole-root transitions

Whole-contribution transitions, including root missing, present-null, and present-value states, encode under the `"$root"` member with a `members` array:

<!-- json-specimen: payload-root -->
```json
{"version":"0.1","changes":[{"member":"$root","before":{"state":"missing","value":null},"after":{"state":"value","value":{"members":[{"member":"Label","value":{"state":"value","value":"present"}},{"member":"RetryCount","value":{"state":"value","value":3}}]}}}]}
```
<!-- /json-specimen -->

Whole-root redacted operations are reported under the same `"$root"` path.

## Baseline-free commands and redaction

A `Patch` enters the envelope with `ChangePayload.FromPatch(patch)`, which redacts every before-state by construction. Mark members whose previous values must not travel with `[SparseRedactBefore]`; `ToPayload()` then emits a redacted before-state while the after-state still travels, including nested models, keyed items, dictionaries, and whole-root before snapshots. A redacted `ToPayload()` transition emits the same shape as a baseline-free command.

<!-- sample: payload-command -->
```csharp
var rotation = new WireSecret.Patch { Password = "rotated-value" };

// A command needs no baseline: the envelope redacts what it never observed.
var command = WireSecret.ChangePayload.FromPatch(rotation);
var commandJson = JsonSerializer.Serialize(command);
// commandJson == {"version":"0.1","changes":[{"member":"Password","before":{"state":"redacted","value":null},"after":{"state":"value","value":"rotated-value"}}]}
var applied = WireSecret
    .Fragment.From(new WireSecret { DisplayName = "a", Password = "previous-placeholder" })
    .Apply(command.ToPatch());
// applied.Password.Value == "rotated-value"
```
<!-- /sample -->

`ToChangeSet()` rebuilds a complete `ChangeSet` and rejects redacted or otherwise incomplete histories instead of fabricating the missing baseline. `ToPatch()` is the explicit baseline-discarding projection and accepts them. `ChangeSet.FromPayload` accepts only fully baseline-aware envelopes and throws a typed error naming the redacted paths.

Redacting the before-value does not protect a sensitive after-value. The after-state still travels, and logs, history displays, diagnostics, validation, and UI must not echo it. Give those surfaces their own handling, and keep placeholder values out of committed samples and logs.

## Conversions

<!-- sample: payload-conversions -->
```csharp
var convertBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var convertAfter = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
);

// ChangeSet -> envelope -> ChangeSet keeps the before-state.
var transition = WireSettings.ChangeSet.Between(convertBefore, convertAfter);
var envelope = transition.ToPayload();
var roundTripped = JsonSerializer
    .Deserialize<WireSettings.ChangePayload>(JsonSerializer.Serialize(envelope))!
    .ToChangeSet();
// roundTripped.ToPatch().Apply(convertBefore) replays convertAfter

// ChangeSet -> Patch discards the before-state explicitly.
// Patch + known baseline -> ChangeSet reattaches it.
var operations = transition.ToPatch();
var reattached = WireSettings.ChangeSet.FromPatch(convertBefore, operations);
// reattached.ToPatch().Apply(convertBefore) replays convertAfter

// Envelope -> ChangeSet requires complete history: FromPayload rejects redacted envelopes.
var complete = WireSettings.ChangeSet.FromPayload(envelope);
// complete.ToPatch().Apply(convertBefore) replays convertAfter
```
<!-- /sample -->

| Conversion | Result | Information kept |
| --- | --- | --- |
| `changes.ToPayload()` | Envelope | Full transition, ready to serialize. |
| `ChangePayload.FromPatch(patch)` | Envelope | Desired operations only; before-states redacted by construction. |
| `payload.ToChangeSet()` | ChangeSet | Complete history. Rejects redacted or incomplete envelopes. |
| `ChangeSet.FromPayload(payload)` | ChangeSet | Same acceptance as `ToChangeSet()`, as a static entry point. |
| `payload.ToPatch()` | Patch | Desired operations for any envelope. Always succeeds and discards baseline information. |
| `ChangeSet.FromPatch(baseline, patch)` | ChangeSet | Attaches a known baseline to an existing patch. |
| `payload.TryApplyMixedTo(model, out updated, out outcome)` | Model plus outcome | Rebases ordinary members, passes write-only members through, applies nothing on conflict. |
| `payload.InvertReversibleChanges(out skipped)` | ChangeSet plus skipped paths | Inverts reversible transitions; write-only paths stay out and appear in `skipped`. |

`ToPatch()` always succeeds and discards baseline information. `FromPayload` and `ToChangeSet` fail on any redacted before-state. After-states must stay concrete (a value, an explicit null where valid, or a missing endpoint for removal); a redacted after-state is malformed. `Redacted` does not mean `Missing`: a redacted before-state paired with a missing after-state is a blind remove that validates against nothing. A merged operation regains complete history only when one side supplied a real baseline; history is never inferred from a redacted endpoint.

## Mixed requests

A member whose before-state arrives redacted is a write-only operation: the sender could not disclose the previous value. Ordinary members in the same request keep ordinary baseline-aware validation and rebase.

<!-- sample: payload-mixed -->
```csharp
var mixed = JsonSerializer.Deserialize<WireSecret.ChangePayload>(
    """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}},{"member":"Password","before":{"state":"redacted","value":null},"after":{"state":"value","value":"rotated-value"}}]}"""
)!;
var mixedCurrent = new WireSecret
{
    DisplayName = "a",
    Password = "current-placeholder",
};
if (!mixed.TryApplyMixedTo(mixedCurrent, out var mixedUpdated, out var mixedOutcome))
{
    throw new InvalidOperationException("The change conflicts with the current model.");
}

// mixedUpdated.DisplayName == "b"
// mixedUpdated.Password == "rotated-value"
// mixedOutcome.WriteOnlyPaths reports ["Password"]
```
<!-- /sample -->

`TryApplyMixedTo` rebases the ordinary members onto the current model and passes the write-only members through without historical comparison. When any ordinary member conflicts, the call returns `false` and applies nothing: the write-only subset is never committed on its own, and the current model is left untouched. The outcome names the write-only paths in `WriteOnlyPaths` and carries the structured conflicts in `Conflicts`. Strict rejection of redacted members is separate opt-in work and does not change the pass-through default.

## Rollback

`InvertReversibleChanges` inverts the reversible transitions and reports the write-only paths it had to skip. A non-empty skipped list means the result is not a complete inverse. `ChangeSet.Invert()` on a complete change set stays a true inversion.

<!-- sample: payload-invert -->
```csharp
var invertBefore = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
);
var invertAfter = Optional<WireSettings.Fragment?>.Present(
    WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
);
var invertible = WireSettings.ChangeSet.Between(invertBefore, invertAfter).ToPayload();

// Fully baseline-aware envelopes invert with nothing skipped.
var rollback = invertible.InvertReversibleChanges(out var skipped);
// skipped is empty
// rollback.ToPatch().Apply(invertAfter) walks back to invertBefore

// Write-only members have no prior value, so they stay out of the rollback.
var writeOnly = WireSecret.ChangePayload.FromPatch(
    new WireSecret.Patch { Password = "rotated-value" }
);
var partial = writeOnly.InvertReversibleChanges(out var skippedWriteOnly);
// skippedWriteOnly reports ["Password"]
// partial carries no member change
```
<!-- /sample -->

## Version and malformed payloads

Every interpretation path (`ToChangeSet`, `ToPatch`, `InvertReversibleChanges`, `TryApplyMixedTo`) validates the envelope wire version before interpreting anything, and rejection never mutates a supplied model.

<!-- sample: payload-version -->
```csharp
var guarded = WireSettings
    .ChangeSet.Between(
        Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before" })
        ),
        Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "after" })
        )
    )
    .ToPayload();
guarded.Version = "0.2";

// Every interpretation path rejects the same unknown version before touching model state.
var versionRejected = 0;
try
{
    guarded.ToChangeSet();
}
catch (ArgumentException)
{
    versionRejected++;
}
try
{
    guarded.ToPatch();
}
catch (ArgumentException)
{
    versionRejected++;
}
// versionRejected == 2
```
<!-- /sample -->

Only `"0.1"` is accepted, compared ordinally. A missing, null, non-string, or different version fails: envelopes that deserialize still fail validation before producing a `ChangeSet`.

Payload DTOs reject unknown JSON members instead of ignoring them. Depending on where the envelope fails, deserialization throws `JsonException` or `ToChangeSet` throws `ArgumentException`. The following are all malformed: unknown member names (including inside nested payloads), null items, members without endpoints, duplicate changes for one member, duplicate keyed changes for one key, keyed additions without an after-endpoint, keyed removals without a before-endpoint, unknown item kinds, redacted after-states, redacted before-states without an after-state, value-carrying missing, null, or redacted endpoints, and per-item redacted endpoints in keyed and dictionary members. Diagnostics carry member paths and reasons only, never secret before, current, or after values. Duplicate JSON properties are the one lenient case: `System.Text.Json` object deserialization is last-wins, so the envelope stays valid when the winning values are well-formed.

## Choosing a conversion

Use `ToChangeSet()` when the receiver must validate history, rebase, or report conflicts. Use `ToPatch()` when the destination only needs the desired operations without validation. Use `TryApplyMixedTo` for envelopes that mix ordinary transitions with write-only operations. Use `InvertReversibleChanges` when rolling back a possibly mixed envelope. When a baseline-free operation is needed instead of a transition, cross the explicit boundary with `ToPatch()`; there is no silent mixed composition back into a `ChangeSet`.
