# Partial Updates

Accept a partial update while preserving the three intents — absent (leave it), `null` (clear it), value (set it). Build the update as a `Patch`, not a full model.

```csharp
var update = new Settings.Patch { Label = (string?)null }; // clear the label
update.Child.Count = 9;                                    // nested set

var remove = new Settings.Patch();
remove.Child.Unset();                                      // drop this contribution

var erase = new Settings.Patch();
erase.Child.SetNull();                                     // explicit null, beats lower layers

var updated = baseline.Apply(update);
```

`Patch.IsEmpty` reports whether an update changes anything at all. Updates arriving as RFC 6902 documents convert with `Patch.FromJsonPatch` and export with `ToJsonPatch` — see [JSON Patch](json-patch.md). Member shapes and construction rules live in [Model shapes](model-shapes.md).
