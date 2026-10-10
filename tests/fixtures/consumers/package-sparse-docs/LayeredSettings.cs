using SparseFragments;

// Canonical compile-checked mirror of docs/how-to/layered-settings.md.
// Library, application, and user fragments share one model: precedence,
// independent nested members, an explicit nullable override, Remove
// fallthrough for a scalar and a nested contribution, and the Fragment
// contribution versus ToModel() distinction. Regions execute as part of Run()
// so the documented effective values are verified, not just compiled.
public static class LayeredSettingsSamples
{
    public static void Run()
    {
        LayeredPrecedence();
        LayeredRemove();
        LayeredMaterialize();
    }

    private static void LayeredPrecedence()
    {
        // sample: layered-precedence
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
        // /sample
        DocsCheck.Require(effective.Theme.IsPresent, "explicit null stays present");
        DocsCheck.Require(effective.Theme.Value is null, "user null overrides library value");
        DocsCheck.Require(effective.Panel.Value!.Title.Value == "Dashboard", "nested Title wins");
        DocsCheck.Require(
            effective.Panel.Value!.PageSize.Value == 10,
            "missing nested member falls through"
        );
    }

    private static void LayeredRemove()
    {
        // sample: layered-remove
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
        // /sample
        DocsCheck.Require(revealed.Theme.Value == "light", "removed scalar falls through");
        DocsCheck.Require(revealed.Panel.Value!.Title.Value == "Dashboard", "nested edit kept");
        DocsCheck.Require(
            nestedRevealed.Panel.Value!.Title.Value == "Home",
            "removed nested member falls through"
        );
        DocsCheck.Require(
            nestedRevealed.Panel.Value!.PageSize.Value == 10,
            "untouched nested member kept"
        );
    }

    private static void LayeredMaterialize()
    {
        // sample: layered-materialize
        var library = new LayeredAppSettings.Fragment { Theme = "light" };
        var user = new LayeredAppSettings.Fragment { Theme = "dark" };
        var effective = library.Merge(user);

        var material = effective.ToModel();
        // material.Theme == "dark"
        material.Theme = "edited";
        // effective.Theme.Value == "dark"
        // /sample
        DocsCheck.Require(material.Theme == "edited", "materialized model edits freely");
        DocsCheck.Require(effective.Theme.Value == "dark", "fragment contribution unchanged");

        // sample: layered-diff
        var beforeModel = new LayeredAppSettings { Theme = "a" };
        var afterModel = new LayeredAppSettings { Theme = "b" };

        var diff = LayeredAppSettings.Fragment.Diff(beforeModel, afterModel);
        // diff.Theme.Value == "b"
        var replayed = LayeredAppSettings.Fragment.From(beforeModel).ApplyChanges(diff);
        // replayed.Theme.Value == "b"
        var transition = beforeModel.CreateChangeSet(afterModel);
        // transition.Theme.Before.Value == "a"
        // transition.Theme.After.Value == "b"
        // /sample
        DocsCheck.Require(diff.Theme.Value == "b", "diff carries the after-value");
        DocsCheck.Require(replayed.Theme.Value == "b", "ApplyChanges replays the diff");
        DocsCheck.Require(
            transition.Theme.Before.Value == "a",
            "change set keeps the before-value"
        );
        DocsCheck.Require(transition.Theme.After.Value == "b", "change set keeps the after-value");
    }
}

// sample: layered-models
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

// /sample
