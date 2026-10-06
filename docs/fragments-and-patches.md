# Fragments and Patches

A fragment is typed partial state: each member is missing, present, or explicitly null. A patch is the difference between two such states. Everything else — layering, persistence, partial updates — composes these two operations.

```csharp
var current = Settings.Fragment.From(new Settings { Label = "a", RetryCount = 1 });

// Layering: present members win, missing members fall through.
var effective = new Settings.Fragment { RetryCount = 5 }.Merge(current);

// Persist only what changed relative to the defaults.
var overrides = Settings.Fragment.Diff(defaults, current);

// Partial update: null clears, missing leaves alone.
var update = new Settings.Patch { Label = (string?)null };
var updated = current.Apply(update);
```

Missing never equals a present value — not even a present `null` or `default` — so `missing → present null`, `present null → missing`, and `missing → present default` are all observable transitions. Member construction, builders, and `DeepClone` follow in [Model shapes](model-shapes.md); per-member merge choice in [Merge strategies](merge-strategies.md).
