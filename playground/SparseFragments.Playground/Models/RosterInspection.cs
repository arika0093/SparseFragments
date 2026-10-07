using System.Text;

namespace SparseFragments.Playground.Models;

/// <summary>
/// Shared typed entry points for Playground Case 3: the <c>Quests</c> keyed
/// transition on <c>PlaygroundRoster.ChangeSet</c>, resolved once, so
/// highlights, summary, and snippets share one vocabulary instead of
/// re-deriving lookups from name strings.
/// </summary>
public static class RosterInspection
{
    /// <summary>Gets the typed <c>Quests</c> keyed transition for a roster change set.</summary>
    public static PlaygroundRoster.ChangeSet.QuestsTransition QuestsTransition(
        PlaygroundRoster.ChangeSet changes
    ) => changes.Quests;

    /// <summary>
    /// Renders the static model semantics for the Case 3 models, independent of
    /// any ChangeSet instance.
    /// </summary>
    public static string DescribeModelMetadata()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Case 3 models: how SparseFragments interprets them.");
        sb.AppendLine("// PlaygroundRoster.Quests: keyed sequence of PlaygroundQuest by Id ([SparseKey]).");
        sb.AppendLine("// PlaygroundQuest.Title: scalar member (typed TitleTransition).");
        sb.AppendLine("// PlaygroundQuest.Points: scalar member (typed PointsTransition).");
        sb.AppendLine("// PlaygroundQuest.Scores: scalar sequence, whole-value transitions.");
        sb.AppendLine("// Observe transitions through PlaygroundRoster.ChangeSet.Quests (typed keyed transition).");
        return sb.ToString();
    }
}
