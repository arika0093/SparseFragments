using SparseTypes;

namespace SparseTypes.Playground.Models;

/// <summary>Keyed quest edited in Case 3; the roster patches this list by <see cref="Id"/>.</summary>
[SparseModel]
public partial class PlaygroundQuest
{
    /// <summary>Gets or sets the stable identity for keyed edits.</summary>
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the points.</summary>
    public int Points { get; set; }

    /// <summary>Gets or sets scalar scores. Scalar sequences patch as whole values.</summary>
    public List<int> Scores { get; set; } = new();
}

/// <summary>Holder for the keyed quest list edited in Case 3.</summary>
[SparseModel]
public partial class PlaygroundRoster
{
    /// <summary>Gets or sets the quests.</summary>
    public List<PlaygroundQuest> Quests { get; set; } = new();
}
