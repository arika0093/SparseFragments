# Layered settings

This guide stacks library, application, and user fragments over one model and
removes overrides layer by layer. It assumes the
[first sparse edit](../tutorial/first-sparse-edit.md) flow already reads
familiar: snapshots, sparse construction, and `Merge` direction. The merge
rules themselves stay in the [merge strategies reference](../merge-strategies.md);
this page shows what they do to a concrete three-layer setup.

## Model and contributions

One model carries a scalar, a nullable scalar, and a nested contribution. The
nested default merges member by member; the scalar replaces. That split is
why one layer can rename the panel without touching its page size.

<!-- sample: layered-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
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

The library ships full defaults. The application renames the panel and says
nothing about the theme or the page size. The user clears the theme with an
explicit `null`, which is present and beats the library value. The table
after the block names each outcome.

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

| Member | Library | Application | User | Effective |
| --- | --- | --- | --- | --- |
| `Theme` | `"light"` | omitted | explicit `null` | present `null` |
| `Panel.Title` | `"Home"` | `"Dashboard"` | omitted | `"Dashboard"` |
| `Panel.PageSize` | `10` | omitted | omitted | `10` |

Omitted means missing and falls through. Explicit `null` is present and
wins over a lower-layer value. A provided value wins over both.

## Remove one contribution to reveal the layer below

`Remove()` on a patch member drops that contribution back to missing. Apply
the removal to the layer that owns the contribution, then merge again: the
next lower present value becomes effective. It never assigns the C# default
or runs a constructor. The scalar case drops the user theme and reveals the
library theme; the nested case drops the whole application panel and reveals
the library panel with its page size intact.

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

## Edit the materialised model without touching the layers

`ToModel()` renders the merged contribution as an ordinary model. Editing
that model changes only the rendering; the fragment layers keep their
presence state. Keep the two apart: change a contribution by patching its
layer, and edit the rendering for local display or handoff.

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

`Fragment.Diff` and `ChangeSet.Between` answer different questions about two
models. `Diff` returns a sparse fragment of after-values for persistence and
defaults comparison; it carries no before-state and cannot invert. A change
set from `CreateChangeSet` keeps both endpoints for audit, undo, and rebase.

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
