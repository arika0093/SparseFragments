# End-to-end change workflow

This tutorial guides you through the full change lifecycle in SparseTypes:
managing draft state in an edit session, inspecting typed diffs, serializing changes
for wire transport, reconciling concurrent updates with rebase, persisting to a database
with EF Core, and composing low-level fragments and patches.

Run it with a .NET 10 or .NET 8 SDK. Create a console project, add `SparseTypes`, and replace `Program.cs`.
Assemble `Program.cs` with the `using` directives first, the executable snippets in order, and the model declaration last. C# top-level statements must precede type declarations.

```shell
dotnet new console -o SparseTutorial --framework net8.0
dotnet add SparseTutorial package SparseTypes
```

## Define the model

The model is a standard C# `partial` class decorated with `[SparseModel]`. Copy it to the bottom of `Program.cs`; the first edit-session snippet supplies the imports at the top.

<!-- sample: tutorial-models -->
```csharp
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }

    public string? Theme { get; set; }

    public int RefreshInterval { get; set; }
}
```
<!-- /sample -->

The generator emits nested companion types (`EditSession`, `ChangeSet`, `ChangePayload`, `Fragment`, and `Patch`) directly inside `UserSettings`. You never write or maintain separate DTO variants.

## Step 1: Manage draft state with EditSession

Start an edit session from an existing model instance. An `EditSession` wraps your data in an observable proxy that tracks dirty properties and raises change notifications. Undo/redo history is opt-in: the session retains nothing until you call `EnableHistory()` (see [Undo and redo](#undo-and-redo-with-session-history)).

Add this at the top of `Program.cs`:

<!-- sample: tutorial-editsession -->
```csharp
using System.Text.Json;
using SparseTypes;

var settings = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};

// 1. Manage draft state with an edit session
var session = settings.CreateEditSession();

session.Observable.Theme = "dark";
// session.HasChanges == true
```
<!-- /sample -->

Editing through `session.Observable` updates the draft and flags `session.HasChanges`. You can call `session.AcceptChanges()` to commit the draft as the new baseline.

### Undo and redo with session history

To step backward and forward through edits, enable the session-owned history first:

<!-- sample: tutorial-history -->
```csharp
var draft = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};
var draftSession = draft.CreateEditSession();

// History is off by default: draftSession.History == null.
var history = draftSession.EnableHistory();

draftSession.Observable.Theme = "dark";
// history.CanUndo == true

history.Undo();
// draft.Theme == "light"

history.Redo();
// draft.Theme == "dark"
```
<!-- /sample -->

`EnableHistory()` returns the session-owned history control and sets `session.History`. `history.Undo()` replays the last transition backward, `history.Redo()` reapplies it, and `session.DisableHistory()` discards retained entries (`History` returns to `null`). Without `EnableHistory`, the session keeps no entries, so there is nothing to undo. Grouped undo steps and history limits are covered in [UI frameworks](../explanation/ui-frameworks.md#edit-history-lifecycle-and-grouping).

## Step 2: Inspect what changed with ChangeSet

When the user finishes editing, inspect the exact modifications using `ChangeSet`. A `ChangeSet` represents a typed before-to-after transition.

<!-- sample: tutorial-changeset -->
```csharp
// 2. Inspect what changed
var changes = session.CreateChangeSet();

// changes.Theme.IsChanged == true
// changes.Theme.Before.Value == "light"
// changes.Theme.After.Value == "dark"
// changes.RefreshInterval.IsChanged == false
```
<!-- /sample -->

Every model property appears on the change set with an `IsChanged` flag and typed `Before` / `After` endpoints. Untouched properties remain `IsChanged == false`, so you only process fields that actually moved.

Calling `changes.Invert()` generates the inverse transition, which walks the state backward without requiring a separate snapshot.

## Step 3: Wire transport with ChangePayload

To send changes across a network boundary (such as from a client UI to a Web API), convert the change set into a lightweight JSON payload.

<!-- sample: tutorial-payload -->
```csharp
// 3. Serialize to wire transport payload
var payload = changes.ToPayload();
var json = JsonSerializer.Serialize(payload);

// Deserialize on the server into a typed ChangeSet
var receivedPayload = JsonSerializer.Deserialize<UserSettings.ChangePayload>(json)!;
var incomingChanges = receivedPayload.ToChangeSet();
```
<!-- /sample -->

`ChangePayload` transfers only modified properties over the wire. On the receiving end, `ToChangeSet()` restores the full typed transition.

## Step 4: Reconcile concurrent changes with Rebase

When client changes reach the server, the server record may have changed concurrently. Rather than overwriting concurrent work with last-write-wins, use three-way rebase to reconcile the changes.

<!-- sample: tutorial-rebase -->
```csharp
// 4. Reconcile concurrent changes
// Suppose another user updated DisplayName on the server in the meantime
var serverCurrent = new UserSettings
{
    DisplayName = "Alice Cooper",
    Theme = "light",
    RefreshInterval = 60,
};

var rebaseResult = incomingChanges.RebaseOnto(serverCurrent);
// rebaseResult.HasConflicts == false
// rebaseResult.Rebased.Theme.After.Value == "dark"
```
<!-- /sample -->

`RebaseOnto` compares the client's original baseline against the latest server state. Independent edits (such as the client updating `Theme` while another user updated `DisplayName`) merge cleanly into `rebaseResult.Rebased`. If both users modified the same property, `rebaseResult.HasConflicts` becomes `true` and surfaces the conflicting property paths.

## Step 5: Persist changes to database with EF Core

Once changes are reconciled, write them to the database. Instead of updating entire entities, map the `ChangeSet` directly onto tracked Entity Framework Core entities with `SparseTypes.EntityFrameworkCore`.

### Design: DTO and Entity separation

Keep your API transfer models (`UserSettings`) separate from database entities (`UserEntity`):

1. **Focused updates**: `mapper.Apply()` marks only modified properties on the EF Core `EntityEntry`. EF Core generates targeted `UPDATE` statements containing only the changed columns.
2. **Prevent lost updates**: Columns touched neither by the client nor the server stay untouched in the database.

### Define the fragment mapper

Annotate a partial class with `[SparseFragmentMapper<TEntity, TDto>]`:

<!-- illustrative: mapper declaration for EF Core entity and DTO -->
```csharp
using SparseTypes.EntityFrameworkCore;

// Database entity (EF Core tracked entity)
public class UserEntity
{
    public int Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public int RefreshInterval { get; set; }
}

[SparseFragmentMapper<UserEntity, UserSettings>]
public sealed partial class UserSettingsMapper
{
    // Matching property names map automatically.
    // Use Configure() only for renamed columns, value conversions, or navigations.
}
```

### Apply and save

Load the tracked entity, apply the rebased changes, and save:

<!-- illustrative: apply changes to EF Core entity and persist -->
```csharp
var mapper = UserSettingsMapper.Create(db.Model);

// 1. Load the target entity
var user = await db.Users.FindAsync(userId);

// 2. Apply only the modified properties to the EF tracker
mapper.Apply(db, user, rebaseResult.Rebased);

// 3. Save to database (generates UPDATE with only modified columns)
await db.SaveChangesAsync();
```

For navigation mapping, collection synchronization, and temporary keys, see the [EF Core mapping guide](../how-to/efcore-mapping.md).

## Step 6: Low-level composition with Fragment and Patch

Underneath `EditSession` and `ChangeSet` lie two foundational primitives: `Fragment` and `Patch`.

* `Fragment`: A sparse snapshot where every property is wrapped in `Optional<T>`. It distinguishes between missing (unassigned), present `null`, and present values. Use fragments to layer defaults and overrides.
* `Patch`: A command object describing target updates without an explicit baseline.

<!-- sample: tutorial-fragment-patch -->
```csharp
// 6. Low-level building blocks: Fragment layering and Patch commands
var defaultLayer = new UserSettings.Fragment { Theme = "light", RefreshInterval = 30 };
var userOverride = new UserSettings.Fragment { Theme = "dark" };

var effective = defaultLayer.Merge(userOverride);
// effective.Theme.Value == "dark"
// effective.RefreshInterval.Value == 30

var patch = new UserSettings.Patch { RefreshInterval = 120 };
var updated = effective.Apply(patch);
// updated.RefreshInterval.Value == 120
```
<!-- /sample -->

`defaultLayer.Merge(userOverride)` overlays the override: present properties take precedence, while missing properties fall through. `effective.Apply(patch)` executes the patch command and returns the resulting fragment.

## Complete runnable Program.cs

This file combines the runnable examples from steps 1, 2, 3, 4, and 6. The EF Core code in step 5 is schematic because it needs an application's `DbContext` and provider configuration; keep it separate from this minimal console program.

<!-- sample: tutorial-program -->
```csharp
using System.Text.Json;
using SparseTypes;

// 1. Manage draft state
var settings = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};
var session = settings.CreateEditSession();
var history = session.EnableHistory();
session.Observable.Theme = "dark";
// history.CanUndo == true
history.Undo();
// settings.Theme == "light"
history.Redo();
// settings.Theme == "dark"

// 2. Inspect what changed
var changes = session.CreateChangeSet();
// changes.Theme.IsChanged == true
// changes.Theme.Before.Value == "light"
// changes.Theme.After.Value == "dark"

// 3. Wire transport payload
var payload = changes.ToPayload();
var json = JsonSerializer.Serialize(payload);
var receivedPayload = JsonSerializer.Deserialize<UserSettings.ChangePayload>(json)!;
var incomingChanges = receivedPayload.ToChangeSet();
// incomingChanges.Theme.IsChanged == true
// incomingChanges.Theme.After.Value == "dark"

// 4. Reconcile concurrent changes
var serverCurrent = new UserSettings
{
    DisplayName = "Alice Cooper",
    Theme = "light",
    RefreshInterval = 60,
};
var rebaseResult = incomingChanges.RebaseOnto(serverCurrent);
// rebaseResult.HasConflicts == false
// rebaseResult.Rebased.Theme.After.Value == "dark"

// 6. Low-level Fragment and Patch
var defaultLayer = new UserSettings.Fragment { Theme = "light", RefreshInterval = 30 };
var userOverride = new UserSettings.Fragment { Theme = "dark" };
var effective = defaultLayer.Merge(userOverride);
var updated = effective.Apply(new UserSettings.Patch { RefreshInterval = 120 });
// effective.Theme.Value == "dark"
// effective.RefreshInterval.Value == 30
// updated.RefreshInterval.Value == 120

Console.WriteLine($"Rebased Theme: {rebaseResult.Rebased.Theme.After.Value}");

// Model declaration at the bottom
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public int RefreshInterval { get; set; }
}
```
<!-- /sample -->

## Where to go next

Now that you have seen the end-to-end lifecycle, explore focused guides for your stack:

* **Interactive UI**: Build form inputs and dirty state indicators with [Blazor edit form](../how-to/blazor-edit-form.md) and [Blazor headless components](../how-to/blazor-headless-components.md).
* **Database persistence**: Map complex entity hierarchies, navigations, and child collections with [EF Core mapping](../how-to/efcore-mapping.md).
* **Concurrent editing**: Dive into three-way merge policies and conflict inspection with [ChangeSet rebase](../explanation/rebase.md).
* **Keyed collections**: Track additions, deletions, and reordering by identity with [Keyed collections](../explanation/keyed-collections.md).
* **Sparse algebra**: Master the four sparse states and layer merging with [Fragments and patches](../explanation/fragments-and-patches.md).
