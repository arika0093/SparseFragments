using SparseFragments;

// Canonical compile-checked mirror of docs/tutorial/first-sparse-edit.md.
// One annotated model and one continuous sequence: baseline snapshot, sparse
// environment override, Merge, Patch command, ChangeSet transition with typed
// Before/After, then Invert. Regions execute in order as part of Run() so the
// documented values are verified, not just compiled.
public static class TutorialFirstEditSamples
{
    public static void Run()
    {
        FirstSparseEdit();
    }

    private static void FirstSparseEdit()
    {
        // sample: tutorial-baseline
        var baselineSettings = new TutorialSiteSettings { Theme = "light", RefreshSeconds = 30 };
        var baseline = TutorialSiteSettings.Fragment.From(baselineSettings);
        // baseline.Theme.Value == "light"
        // baseline.RefreshSeconds.Value == 30
        // /sample
        DocsCheck.Require(baseline.Theme.Value == "light", "baseline snapshots Theme");
        DocsCheck.Require(baseline.RefreshSeconds.Value == 30, "baseline snapshots RefreshSeconds");

        // sample: tutorial-override
        var environmentOverride = new TutorialSiteSettings.Fragment { RefreshSeconds = 60 };
        // environmentOverride.Theme.IsPresent == false
        // environmentOverride.RefreshSeconds.Value == 60
        // /sample
        DocsCheck.Require(!environmentOverride.Theme.IsPresent, "unassigned member stays missing");
        DocsCheck.Require(
            environmentOverride.RefreshSeconds.Value == 60,
            "override carries RefreshSeconds"
        );

        // sample: tutorial-merge
        var effective = baseline.Merge(environmentOverride);
        // effective.Theme.Value == "light"
        // effective.RefreshSeconds.Value == 60
        // /sample
        DocsCheck.Require(effective.Theme.Value == "light", "missing member falls through");
        DocsCheck.Require(effective.RefreshSeconds.Value == 60, "present member wins");

        // sample: tutorial-patch
        var command = new TutorialSiteSettings.Patch { Theme = "dark" };
        var updated = effective.Apply(command);
        // updated.Theme.Value == "dark"
        // updated.RefreshSeconds.Value == 60
        // /sample
        DocsCheck.Require(updated.Theme.Value == "dark", "patch sets Theme");
        DocsCheck.Require(updated.RefreshSeconds.Value == 60, "patch keeps RefreshSeconds");

        // sample: tutorial-changeset
        var tutorialBefore = Optional<TutorialSiteSettings.Fragment?>.Present(effective);
        var tutorialAfter = Optional<TutorialSiteSettings.Fragment?>.Present(updated);
        var changes = TutorialSiteSettings.ChangeSet.Between(tutorialBefore, tutorialAfter);
        // changes.Theme.IsChanged == true
        // changes.Theme.Before.Value == "light"
        // changes.Theme.After.Value == "dark"
        // changes.RefreshSeconds.IsChanged == false
        // /sample
        DocsCheck.Require(changes.Theme.IsChanged, "Theme transition observed");
        DocsCheck.Require(changes.Theme.Before.Value == "light", "Before preserves the old value");
        DocsCheck.Require(changes.Theme.After.Value == "dark", "After preserves the new value");
        DocsCheck.Require(!changes.RefreshSeconds.IsChanged, "untouched member stays unchanged");

        // sample: tutorial-invert
        var undone = changes.Invert();
        // undone.ToPatch().Apply(tutorialAfter) replays tutorialBefore
        // /sample
        DocsCheck.Require(
            TutorialSiteSettings
                .Patch.Between(undone.ToPatch().Apply(tutorialAfter), tutorialBefore)
                .IsEmpty,
            "inverted transition walks back"
        );
    }
}

// sample: tutorial-models
[SparseFragmentModel]
public partial class TutorialSiteSettings
{
    public string? Theme { get; set; }

    public int RefreshSeconds { get; set; }
}

// /sample
