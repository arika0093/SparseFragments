using SparseTypes;
using SparseTypes.Generated;

public static class EditSessionGroupsDocsSamples
{
    public static void Run()
    {
        MultiSessionSubmission();
    }

    private static void MultiSessionSubmission()
    {
        // sample: edit-session-group-flow
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
        // /sample

        DocsCheck.Require(!profileSession.HasChanges, "profile baseline advanced");
        DocsCheck.Require(settingsSession.HasChanges, "settings remains dirty");
        DocsCheck.Require(
            profileChanges.DisplayName.After.Value == "Alice Cooper",
            "captured change reflects observable edit"
        );
        DocsCheck.Require(
            settingsChanges.Theme.After.Value == "dark",
            "settings change reflects observable edit"
        );
    }
}

// sample: edit-session-group-models
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
// /sample
