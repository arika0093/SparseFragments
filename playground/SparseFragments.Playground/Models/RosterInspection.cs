using System.Text;
using SparseFragments;

namespace SparseFragments.Playground.Models;

/// <summary>
/// Shared inspection entry points for Playground Case 3: the <c>T.Sparse.Properties</c>
/// descriptors (#72) resolved once, so highlights, summary, and snippets share one
/// property-identity vocabulary instead of re-deriving lookups from name strings.
/// </summary>
public static class RosterInspection
{
    /// <summary>Gets the <c>Quests</c> descriptor from <c>PlaygroundRoster.Sparse.Properties</c>.</summary>
    public static SparsePropertyInfo QuestsProperty { get; } =
        PlaygroundRoster.Sparse.Properties.Single(static property =>
            property.Name == nameof(PlaygroundRoster.Quests)
        );

    /// <summary>Gets the <c>Title</c> descriptor from <c>PlaygroundQuest.Sparse.Properties</c>.</summary>
    public static SparsePropertyInfo QuestTitleProperty { get; } =
        PlaygroundQuest.Sparse.Properties.Single(static property =>
            property.Name == nameof(PlaygroundQuest.Title)
        );

    /// <summary>Gets the <c>Points</c> descriptor from <c>PlaygroundQuest.Sparse.Properties</c>.</summary>
    public static SparsePropertyInfo QuestPointsProperty { get; } =
        PlaygroundQuest.Sparse.Properties.Single(static property =>
            property.Name == nameof(PlaygroundQuest.Points)
        );

    /// <summary>Gets the <c>Scores</c> descriptor from <c>PlaygroundQuest.Sparse.Properties</c>.</summary>
    public static SparsePropertyInfo QuestScoresProperty { get; } =
        PlaygroundQuest.Sparse.Properties.Single(static property =>
            property.Name == nameof(PlaygroundQuest.Scores)
        );

    /// <summary>
    /// Finds the <c>Quests</c> change by descriptor identity: <c>change.Property</c>
    /// is the same instance as the matching <c>T.Sparse.Properties</c> entry (#73).
    /// </summary>
    public static SparsePatchChange? QuestsChange(PlaygroundRoster.Patch patch) =>
        patch.Changes.FirstOrDefault(static change =>
            ReferenceEquals(change.Property, QuestsProperty)
        );

    /// <summary>
    /// Renders the static model semantics enumerated directly from
    /// <c>T.Sparse.Properties</c> (#72), independent of any Patch instance.
    /// </summary>
    public static string DescribeModelMetadata()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// T.Sparse.Properties: how SparseFragments interprets the Case 3 models (#72).");
        AppendModelMetadata(sb, nameof(PlaygroundRoster), PlaygroundRoster.Sparse.Properties);
        AppendModelMetadata(sb, nameof(PlaygroundQuest), PlaygroundQuest.Sparse.Properties);
        return sb.ToString();
    }

    private static void AppendModelMetadata(
        StringBuilder sb,
        string modelName,
        IReadOnlyList<SparsePropertyInfo> properties
    )
    {
        sb.AppendLine($"// {modelName}.Sparse.Properties ({properties.Count}):");
        foreach (var property in properties)
        {
            var keys =
                property.KeyPropertyNames.Count == 0
                    ? "-"
                    : string.Join(",", property.KeyPropertyNames);
            sb.AppendLine(
                $"//   {property.Name}: {property.PropertyType.Name}"
                    + $" | nested={property.IsNestedModel}"
                    + $" | merge={property.MergeMode}"
                    + $" | collection={property.CollectionSemantic}"
                    + $" | key={property.KeyKind}[{keys}]"
            );
        }
    }
}
