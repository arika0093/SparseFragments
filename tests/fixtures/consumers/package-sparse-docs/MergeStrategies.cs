using SparseFragments;

// Canonical compile-checked mirror of docs/merge-strategies.md (#45).
// Covers the built-in modes users configure per member (explicit Replace,
// Deep for nested models, Append, SetUnion; unconfigured members use
// MergeMode.Default with the same type-dependent behavior) and the custom
// FragmentMergeStrategy<T> extension point.
public static class MergeStrategiesSamples
{
    public static void Run()
    {
        AppendConcatenates();
        SetUnionCombines();
        DeepMergesNestedModels();
        CustomStrategy();
    }

    private static void AppendConcatenates()
    {
        var lower = MergeDocsSettings.Fragment.From(
            new MergeDocsSettings { Plugins = new List<string> { "base-plugin" } });
        var higher = new MergeDocsSettings.Fragment
        {
            Plugins = new List<string> { "extra-plugin" },
        };
        var merged = lower.Merge(higher).ToModel();
        DocsCheck.Require(
            merged.Plugins.SequenceEqual(new[] { "base-plugin", "extra-plugin" }),
            "merge Append concatenates lowest to highest priority");
    }

    private static void SetUnionCombines()
    {
        var lower = MergeDocsSettings.Fragment.From(
            new MergeDocsSettings { Tags = new HashSet<string>(StringComparer.Ordinal) { "a" } });
        var higher = new MergeDocsSettings.Fragment
        {
            Tags = new HashSet<string>(StringComparer.Ordinal) { "a", "b" },
        };
        var merged = lower.Merge(higher).ToModel();
        DocsCheck.Require(
            merged.Tags.SetEquals(new HashSet<string>(StringComparer.Ordinal) { "a", "b" }),
            "merge SetUnion combines as an insertion-ordered set union");
    }

    private static void DeepMergesNestedModels()
    {
        var lower = MergeDocsSettings.Fragment.From(
            new MergeDocsSettings { Child = new MergeDocsChild { Host = "db.local" } });
        var higher = new MergeDocsSettings.Fragment
        {
            Child = new MergeDocsChild.Fragment { Count = 9 },
        };
        var merged = lower.Merge(higher).ToModel();
        DocsCheck.Require(
            merged.Child!.Host == "db.local" && merged.Child.Count == 9,
            "merge Deep recurses member by member");
    }

    private static void CustomStrategy()
    {
        var lower = MergeDocsPolicy.Fragment.From(new MergeDocsPolicy { Note = "lower" });
        var higher = new MergeDocsPolicy.Fragment { Note = "higher" };
        DocsCheck.Require(
            lower.Merge(higher).ToModel().Note == "higher",
            "custom FragmentMergeStrategy present value wins");
        var missing = new MergeDocsPolicy.Fragment();
        DocsCheck.Require(
            lower.Merge(missing).ToModel().Note == "lower",
            "custom FragmentMergeStrategy missing falls through");
    }
}

[SparseFragmentModel]
public partial class MergeDocsSettings
{
    public string? Label { get; set; }

    public MergeDocsChild? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public List<string> Plugins { get; set; } = new();

    [SparseMerge(MergeMode.SetUnion)]
    public HashSet<string> Tags { get; set; } = new(StringComparer.Ordinal);
}

public partial class MergeDocsChild
{
    public int Count { get; set; }

    public string Host { get; set; } = "localhost";
}

// Custom per-member algebra: delegates to FragmentMergeStrategy<T> via
// [SparseMerge(typeof(...))]. Instances are shared by generated code, so the
// strategy stays stateless.
public sealed class DocsLastWriteStrategy : FragmentMergeStrategy<string?>
{
    public override Optional<string?> Merge(
        Optional<string?> lowerPriority,
        Optional<string?> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(string? left, string? right) => left == right;
}

[SparseFragmentModel]
public partial class MergeDocsPolicy
{
    [SparseMerge(typeof(DocsLastWriteStrategy))]
    public string? Note { get; set; }
}
