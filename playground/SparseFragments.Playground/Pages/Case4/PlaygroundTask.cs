using SparseFragments;

namespace SparseFragments.Playground.Models;

/// <summary>Keyed task edited in Case 4.</summary>
[SparseFragmentModel]
public partial class PlaygroundTask
{
    /// <summary>Gets or sets the stable identity.</summary>
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the points.</summary>
    public int Points { get; set; }
}
