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

### Retrieving section changes
Call `submission.Get(handle)` with the typed handle returned during registration. It returns an `EditSessionGroupSectionSubmission<TModel, TChangeSet>` containing:
* `Changes`: The typed `ChangeSet` snapshot for that section.
* `ChangedPaths`: Canonical paths modified within the section.
* `BaselineRevision`: Group revision captured when the session was registered or last acknowledged.

### Independent acknowledgement
Call `group.AcceptChanges(submission, handle)` when a specific backend service successfully persists that section's changes.
* Acknowledging one section advances only its baseline and revision.
* Unacknowledged sections remain dirty (`HasChanges == true`).
* Edits made to a session after `group.Prepare()` remain pending after `AcceptChanges` completes, preserving concurrent user typing.

### Server normalization and reconciliation
When the persistence layer mutates submitted entities (e.g. generating server timestamps or normalizing values), call `group.TryReconcile(submission, handle, persistedModel, out error)` to update the session baseline without clobbering subsequent uncommitted client edits.

### Rejection of open drafts and stale submissions
* `group.Prepare()` throws `EditSessionGroupValidationException` if any registered session contains active uncommitted child scopes (`OpenScope`). Cancel or commit child scopes before preparing a submission.
* `AcceptChanges` and `TryReconcile` throw `EditSessionStaleSubmissionException` if called with a previously acknowledged or outdated submission.
