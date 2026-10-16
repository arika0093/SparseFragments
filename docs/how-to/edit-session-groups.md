# Coordinate multi-session submissions with EditSessionGroup

This guide shows how to register multiple edit sessions across distinct model types into an `EditSessionGroup`, validate and snapshot changes into a coordinated submission, and acknowledge saved sections independently.

## Model setup

Declare model types with `[SparseModel]`:

<!-- sample: edit-session-group-models -->
```csharp
[SparseModel]
public partial class GroupUserProfile
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

[SparseModel]
public partial class GroupUserSettings
{
    public string Theme { get; set; } = "dark";
    public bool Notifications { get; set; } = true;
}
```
<!-- /sample -->

## Registration and submission workflow

<!-- sample: edit-session-group-flow -->
```csharp
var profile = new GroupUserProfile { DisplayName = "Alice", Email = "alice@example.com" };
var profileSession = profile.CreateEditSession();

var settings = new GroupUserSettings { Theme = "light", Notifications = false };
var settingsSession = settings.CreateEditSession();

var group = new EditSessionGroup();
var profileHandle = group.Register(
    "profile",
    new EditSessionGroupRegistration<GroupUserProfile, GroupUserProfile.ChangeSet>
    {
        Session = profileSession,
        CreateChangeSet = profileSession.CreateChangeSet,
        IsEmpty = static changes => changes.IsEmpty,
        EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
    }
);
var settingsHandle = group.Register(
    "settings",
    new EditSessionGroupRegistration<GroupUserSettings, GroupUserSettings.ChangeSet>
    {
        Session = settingsSession,
        CreateChangeSet = settingsSession.CreateChangeSet,
        IsEmpty = static changes => changes.IsEmpty,
        EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
    }
);

profileSession.Observable.DisplayName = "Alice Cooper";
settingsSession.Observable.Theme = "dark";

// Snapshot dirty sections into a coordinated submission
var submission = group.Prepare();

var profileChanges = submission.Get(profileHandle).Changes;
var settingsChanges = submission.Get(settingsHandle).Changes;
// profileChanges.DisplayName.After.Value == "Alice Cooper"
// settingsChanges.Theme.After.Value == "dark"

// Suppose profile service persists successfully, but settings service is deferred or fails:
group.AcceptChanges(submission, profileHandle);

// Profile baseline advanced; settings section remains dirty
// profileSession.HasChanges == false
// settingsSession.HasChanges == true
```
<!-- /sample -->

## Working with section submissions

* **Retrieve section changes**: Call `submission.Get(handle).Changes` to access the typed `ChangeSet` snapshot for that registered model.
* **Acknowledge saved sections independently**: Call `group.AcceptChanges(submission, handle)` when a specific section is persisted. Only that section's baseline advances; unpersisted sections remain dirty (`HasChanges == true`).
* **Preserve concurrent user typing**: Edits made to a session after `group.Prepare()` remain pending after `AcceptChanges` completes.
* **Server normalization**: Call `group.TryReconcile(submission, handle, persistedModel, out error)` to adopt server-modified properties without overwriting pending client edits.
* **Keep baseline ownership in the group**: While a session is registered, acknowledge or reconcile it through the group. Generated sessions report direct baseline changes; custom sessions without `ISparseEditSessionWithBaselineRevision` must remain under exclusive group ownership.

For deep dive into session group coordination and migration rules, see [Edit-session groups explanation](../explanation/edit-session-groups.md).
