namespace SparseFragments.Playground.Models;

/// <summary>
/// Default Case 3 rosters, constructed directly as generated models.
/// </summary>
public static class RosterDefaults
{
    /// <summary>Creates the default before roster.</summary>
    public static PlaygroundRoster Before()
    {
        return new PlaygroundRoster
        {
            Quests = new List<PlaygroundQuest>
            {
                new() { Id = "a", Title = "First", Points = 10, Scores = new List<int> { 10, 20 } },
                new() { Id = "b", Title = "Second", Points = 20, Scores = new List<int> { 30 } },
                new() { Id = "c", Title = "Third", Points = 30, Scores = new List<int> { 40, 50 } },
            },
        };
    }

    /// <summary>Creates the default after roster (remove a, retitle and update b, move c, add a score to c, add d).</summary>
    public static PlaygroundRoster After()
    {
        return new PlaygroundRoster
        {
            Quests = new List<PlaygroundQuest>
            {
                new() { Id = "c", Title = "Third", Points = 30, Scores = new List<int> { 40, 50, 60 } },
                new() { Id = "b", Title = "Second v2", Points = 25, Scores = new List<int> { 30, 31 } },
                new() { Id = "d", Title = "Fourth", Points = 5, Scores = new List<int> { 60 } },
            },
        };
    }
}
