using SparseFragments;

namespace CollaborativeEditing.Contracts;

/// <summary>Workspace settings edited by clients.</summary>
[SparseFragmentModel]
public partial class WorkspaceSettings
{
    /// <summary>Gets or sets the UI theme name.</summary>
    public string Theme { get; set; } = "light";

    /// <summary>Gets or sets the retry count.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Gets or sets whether notifications are enabled.</summary>
    public bool Notifications { get; set; } = true;
}

/// <summary>Keyed quest edited by clients.</summary>
[SparseFragmentModel]
public partial class Quest
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

/// <summary>Editable workspace aggregate.</summary>
[SparseFragmentModel]
public partial class Workspace
{
    /// <summary>Gets or sets the workspace name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the user-override settings.</summary>
    public WorkspaceSettings Settings { get; set; } = new();

    /// <summary>Gets or sets the keyed quests.</summary>
    public List<Quest> Quests { get; set; } = new();
}

/// <summary>Workspace with its aggregate revision.</summary>
/// <param name="Workspace">The workspace model.</param>
/// <param name="Revision">The aggregate revision.</param>
/// <param name="EffectiveSettings">Settings after layering system defaults under the override.</param>
public sealed record WorkspaceSnapshot(
    Workspace Workspace,
    long Revision,
    WorkspaceSettings EffectiveSettings
);
