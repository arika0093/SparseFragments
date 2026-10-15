using SparseTypes.Playground.Models;

namespace SparseTypes.Playground.Pages.Case3;

/// <summary>Reference-based render positions for roster rows.</summary>
/// <remarks>The parent builds the map once per refresh so rows share one O(N) pass instead of each scanning the list. Callbacks still resolve the live index by reference and validate it, because a render-time position may be stale after a reorder.</remarks>
public static class RosterPositions
{
    /// <summary>Builds the row-to-index map for the current list order.</summary>
    /// <param name="quests">The list in render order.</param>
    /// <returns>Each instance's position; a repeated instance keeps its last position.</returns>
    public static IReadOnlyDictionary<PlaygroundQuest, int> Build(
        IReadOnlyList<PlaygroundQuest> quests
    )
    {
        ArgumentNullException.ThrowIfNull(quests);

        var positions = new Dictionary<PlaygroundQuest, int>(
            quests.Count,
            ReferenceEqualityComparer.Instance
        );
        for (var index = 0; index < quests.Count; index++)
        {
            positions[quests[index]] = index;
        }

        return positions;
    }

    /// <summary>Reads a row's render-time position, or -1 when the row is no longer listed.</summary>
    /// <param name="positions">The map built by <see cref="Build"/>.</param>
    /// <param name="quest">The row instance.</param>
    /// <returns>The render-time position, or -1.</returns>
    public static int GetPosition(
        IReadOnlyDictionary<PlaygroundQuest, int> positions,
        PlaygroundQuest quest
    )
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(quest);

        return positions.TryGetValue(quest, out var position) ? position : -1;
    }
}
