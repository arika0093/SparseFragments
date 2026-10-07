using SparseFragments;

// Canonical compile-checked mirror of docs/ui-frameworks.md (#48).
// Covers the shared UI editing model: bind the generated T.Observable proxy,
// keep editing the underlying live model, and derive patches from
// baseline-versus-current comparison.
public static class UiFrameworksSamples
{
    public static void Run()
    {
        ObservableBinding();
        PatchFromBaselineVersusCurrent();
    }

    private static void ObservableBinding()
    {
        var model = new UiWidget { Title = "a", Child = new UiWidgetChild { Name = "n" } };
        var seen = new List<string>();
        var observable = new UiWidget.Observable(model);
        observable.PropertyChanged += (_, args) => seen.Add(args.PropertyName ?? "<null>");

        observable.Title = "b";
        DocsCheck.Require(model.Title == "b", "observable writes through to the model");
        DocsCheck.Require(
            seen.Count == 1 && seen[0] == "Title",
            "scalar set raises PropertyChanged");

        observable.Title = "b";
        DocsCheck.Require(seen.Count == 1, "same value does not notify");

        observable.Child!.Name = "n2";
        DocsCheck.Require(model.Child!.Name == "n2", "nested proxy edits the model");
    }

    private static void PatchFromBaselineVersusCurrent()
    {
        var model = new UiWidget { Title = "a" };
        var baseline = UiWidget.Fragment.From(model);

        model.Title = "b";
        var changes = UiWidget.ChangeSet.Between(baseline, UiWidget.Fragment.From(model));
        DocsCheck.Require(!changes.IsEmpty, "baseline/current diff is the patch source");

        model.Title = "a";
        var restored = UiWidget.ChangeSet.Between(baseline, UiWidget.Fragment.From(model));
        DocsCheck.Require(restored.IsEmpty, "edit-then-restore is empty");
    }
}

[SparseFragmentModel]
public partial class UiWidgetChild
{
    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class UiWidget
{
    public string Title { get; set; } = string.Empty;

    public UiWidgetChild? Child { get; set; }
}
