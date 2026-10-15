# Layered settings

This guide shows how to compose multi-layer configuration (defaults, application overrides, and user settings) and remove overrides to reveal the layer beneath.

## Model and contributions

Define a model with scalar and nested properties:

<!-- sample: layered-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class LayeredAppSettings
{
    public string? Theme { get; set; }

    public LayeredPanel? Panel { get; set; }
}

public partial class LayeredPanel
{
    public string? Title { get; set; }

    public int PageSize { get; set; }
}
```
<!-- /sample -->

## Merge layers

Combine fragments with `Merge()`. The argument takes precedence over the receiver; omitted members remain missing and fall through to lower layers:

<!-- sample: layered-precedence -->
```csharp
var library = new LayeredAppSettings.Fragment
{
    Theme = "light",
    Panel = new LayeredPanel.Fragment { Title = "Home", PageSize = 10 },
};
var application = new LayeredAppSettings.Fragment
{
    Panel = new LayeredPanel.Fragment { Title = "Dashboard" },
};
var user = new LayeredAppSettings.Fragment { Theme = (string?)null };

var effective = library.Merge(application).Merge(user);
// effective.Theme.IsPresent == true
// effective.Theme.Value is null
// effective.Panel.Value!.Title.Value == "Dashboard"
// effective.Panel.Value!.PageSize.Value == 10
```
<!-- /sample -->

* **Provided value**: Overrides lower layers.
* **Explicit `null`**: Present and overrides lower values with `null`.
* **Omitted (missing)**: Falls through to the next lower layer.

## Remove overrides to reveal lower layers

To clear an override and reveal the lower layer, call `Remove()` on a patch and apply it to that layer:

<!-- sample: layered-remove -->
```csharp
var library = new LayeredAppSettings.Fragment
{
    Theme = "light",
    Panel = new LayeredPanel.Fragment { Title = "Home", PageSize = 10 },
};
var application = new LayeredAppSettings.Fragment
{
    Panel = new LayeredPanel.Fragment { Title = "Dashboard" },
};
var user = new LayeredAppSettings.Fragment { Theme = (string?)null };

var removeTheme = new LayeredAppSettings.Patch();
removeTheme.Theme.Remove();
var userWithoutTheme = user.Apply(removeTheme);

var revealed = library.Merge(application).Merge(userWithoutTheme);
// revealed.Theme.Value == "light"
// revealed.Panel.Value!.Title.Value == "Dashboard"

var removePanel = new LayeredAppSettings.Patch();
removePanel.Panel.Remove();
var applicationWithoutPanel = application.Apply(removePanel);

var nestedRevealed = library.Merge(applicationWithoutPanel).Merge(userWithoutTheme);
// nestedRevealed.Panel.Value!.Title.Value == "Home"
// nestedRevealed.Panel.Value!.PageSize.Value == 10
```
<!-- /sample -->

## Materialize to an ordinary model

Call `ToModel()` to convert the merged fragment into a standard C# model instance for display or consumption:

<!-- sample: layered-materialize -->
```csharp
var library = new LayeredAppSettings.Fragment { Theme = "light" };
var user = new LayeredAppSettings.Fragment { Theme = "dark" };
var effective = library.Merge(user);

var material = effective.ToModel();
// material.Theme == "dark"
material.Theme = "edited";
// effective.Theme.Value == "dark"
```
<!-- /sample -->

To persist differences against defaults, use `Fragment.Diff(before, after)`. To track transitions with before/after state for audit or undo, use `before.CreateChangeSet(after)`:

<!-- sample: layered-diff -->
```csharp
var beforeModel = new LayeredAppSettings { Theme = "a" };
var afterModel = new LayeredAppSettings { Theme = "b" };

var diff = LayeredAppSettings.Fragment.Diff(beforeModel, afterModel);
// diff.Theme.Value == "b"
var replayed = LayeredAppSettings.Fragment.From(beforeModel).ApplyChanges(diff);
// replayed.Theme.Value == "b"
var transition = beforeModel.CreateChangeSet(afterModel);
// transition.Theme.Before.Value == "a"
// transition.Theme.After.Value == "b"
```
<!-- /sample -->

For detailed merge modes and collection behaviors, see [Merge strategies](../explanation/merge-strategies.md).
