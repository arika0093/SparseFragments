using SparseFragments;

// Canonical compile-checked mirror of docs/cloning-and-ownership.md (#45).
// Covers the ownership rule of thumb (assignment shares; construction from a
// model snapshots), builders, DeepClone isolation, and the
// [SparseCloneReferenceSafe] opt-out for intentionally shared references.
public static class CloningAndOwnershipSamples
{
    public static void Run()
    {
        AssignmentSharesReferences();
        FromSnapshots();
        BuilderAndDeepClone();
        ReferenceSafeMembersAreShared();
    }

    private static void AssignmentSharesReferences()
    {
        var tags = new List<string> { "a" };
        var patch = new CloneDocsSettings.Patch { Plugins = tags };
        var result = new CloneDocsSettings.Fragment().Apply(patch);

        // Nothing is cloned on assignment or on Apply: one shared list.
        tags.Add("b");
        DocsCheck.Require(
            result.Plugins.Value!.SequenceEqual(new[] { "a", "b" }),
            "typed patch assignment shares references");
    }

    private static void FromSnapshots()
    {
        var model = new CloneDocsSettings
        {
            Label = "original",
            Child = new CloneDocsChild { Count = 7, Host = "keep" },
        };
        var fragment = CloneDocsSettings.Fragment.From(model);
        model.Label = "mutated";
        model.Child!.Count = 42;
        DocsCheck.Require(
            fragment.Label.Value == "original" && fragment.Child.Value!.Count.Value == 7,
            "Fragment.From snapshots an isolated copy");
    }

    private static void BuilderAndDeepClone()
    {
        var original = CloneDocsSettings.Fragment.From(
            new CloneDocsSettings
            {
                Label = "original",
                Child = new CloneDocsChild { Count = 7, Host = "keep" },
            });

        var builder = original.ToBuilder();
        builder.Label = Optional<string?>.Missing;
        var edited = builder.Build();
        DocsCheck.Require(!edited.Label.IsPresent, "builder copy without member");

        var clone = original.ToModel().DeepClone();
        clone.Child!.Count = 42;
        DocsCheck.Require(
            original.ToModel().Child!.Count == 7,
            "DeepClone structural isolation");
        var fragmentClone = original.DeepClone();
        DocsCheck.Require(
            fragmentClone.Label.Value == "original",
            "fragment DeepClone preserves values");
    }

    private static void ReferenceSafeMembersAreShared()
    {
        var service = new DocsSharedService();
        var fragment = CloneDocsServiceSettings.Fragment.From(
            new CloneDocsServiceSettings { Service = service });
        var model = fragment.ToModel();

        // DocsSharedService has no cloneable state, so construction carries
        // the reference over instead of requiring a deep-cloneable shape.
        DocsCheck.Require(
            ReferenceEquals(model.Service, service),
            "Fragment.From carries reference-safe members over");

        // DeepClone does the same: the service instance survives cloning.
        var clone = model.DeepClone();
        DocsCheck.Require(
            ReferenceEquals(clone.Service, service),
            "SparseCloneReferenceSafe carries the reference over");
    }
}

[SparseFragmentModel]
public partial class CloneDocsSettings
{
    public string? Label { get; set; }

    public CloneDocsChild? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public List<string> Plugins { get; set; } = new();
}

public partial class CloneDocsChild
{
    public int Count { get; set; }

    public string Host { get; set; } = "localhost";
}

public sealed class DocsSharedService
{
    public void Touch() { }
}

[SparseFragmentModel]
public partial class CloneDocsServiceSettings
{
    // Non-partial nested types stay atomic whole values: Replace keeps that
    // explicit (SPF007), while SparseCloneReferenceSafe carries the reference
    // over instead of requiring a deep-cloneable shape.
    [SparseMerge(MergeMode.Replace)]
    [SparseCloneReferenceSafe]
    public DocsSharedService Service { get; set; } = null!;
}
