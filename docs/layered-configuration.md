# Layered Configuration

Combine defaults, environment overrides, and user overrides into one effective model. Each layer is a fragment carrying only what it specifies; `Merge` overlays them by priority.

```csharp
var defaults = new AppSettings.Fragment { Theme = "light", RetryCount = 3 };
var environment = new AppSettings.Fragment { RetryCount = 5 };
var user = new AppSettings.Fragment { Theme = "dark" };

var effective = defaults.Merge(environment).Merge(user).ToModel();
// effective.Theme == "dark", effective.RetryCount == 5
```

Missing members fall through to the lower layer; present members — including explicit `null` — win. Persisting only what differs from defaults is the same pattern: `AppSettings.Fragment.Diff(defaults, current)` keeps just the overrides. See [Merge strategies](merge-strategies.md) for per-member composition rules and [Model shapes](model-shapes.md) for fragment construction.
