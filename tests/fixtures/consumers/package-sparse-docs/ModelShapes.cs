using SparseFragments;

// Canonical compile-checked mirror of docs/model-shapes.md (#45).
// Covers the root model requirements ([SparseFragmentModel] on a partial
// class) and nested structural promotion: reachable partial nested types
// automatically receive generated Fragment/Patch APIs without their own
// annotation.
public static class ModelShapesSamples
{
    public static void Run()
    {
        PromotedNestedTypesCarryFragmentApis();
        NestedDiffAndPatch();
    }

    private static void PromotedNestedTypesCarryFragmentApis()
    {
        // ShapeDocsChild is partial but undecorated: promotion still generates
        // ShapeDocsChild.Fragment, which sparse construction can use directly.
        var sparse = new ShapeDocsSettings.Fragment
        {
            Label = "base",
            Child = new ShapeDocsChild.Fragment { Count = 9 },
        };
        DocsCheck.Require(sparse.Label.IsPresent, "root sparse construction");
        DocsCheck.Require(
            sparse.Child.Value!.Count.Value == 9,
            "promoted nested fragment constructs sparsely");
        DocsCheck.Require(
            !sparse.Child.Value.Host.IsPresent,
            "promoted nested fragment leaves members missing");
    }

    private static void NestedDiffAndPatch()
    {
        var before = new ShapeDocsSettings
        {
            Label = "before",
            Child = new ShapeDocsChild { Count = 1, Host = "a" },
        };
        var after = new ShapeDocsSettings
        {
            Label = "after",
            Child = new ShapeDocsChild { Count = 2, Host = "a" },
        };
        var diff = ShapeDocsSettings.Fragment.Diff(before, after);
        var result = ShapeDocsSettings.Fragment.From(before).ApplyChanges(diff);
        DocsCheck.Require(
            result.Label.Value == "after" && result.Child.Value!.Count.Value == 2,
            "nested Diff/ApplyChanges");

        var patch = new ShapeDocsSettings.Patch();
        patch.Child.Host = "b";
        var updated = ShapeDocsSettings.Fragment.From(before).Apply(patch);
        DocsCheck.Require(
            updated.Child.Value!.Host.Value == "b" && updated.Child.Value.Count.Value == 1,
            "typed nested patch through promoted type");
    }
}

[SparseFragmentModel]
public partial class ShapeDocsSettings
{
    public string? Label { get; set; }

    public ShapeDocsChild? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}

public partial class ShapeDocsChild
{
    public int Count { get; set; }

    public string Host { get; set; } = "localhost";
}
