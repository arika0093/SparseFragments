using System.Text.Json.Serialization;
using SparseFragments;

namespace SparseFragments.Playground.Models;

/// <summary>Demo model edited in the playground.</summary>
[SparseFragmentModel]
public partial class PlaygroundSettings
{
    /// <summary>Gets or sets a flag.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a retry count.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Gets or sets a label.</summary>
    public string? Label { get; set; } = "default";

    /// <summary>Gets or sets a nested model.</summary>
    public PlaygroundNested? Nested { get; set; } = new();

    /// <summary>Gets or sets plugins. Lower and higher layers are concatenated.</summary>
    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}

/// <summary>Demo nested model edited in the playground.</summary>
[SparseFragmentModel]
public partial class PlaygroundNested
{
    /// <summary>Gets or sets a host name.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Gets or sets a port number.</summary>
    public int Port { get; set; } = 5432;
}

/// <summary>Keyed element edited in section 3. The outer list patches by <c>Id</c>.</summary>
[SparseFragmentModel]
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

/// <summary>Holder for the keyed quest list edited in section 3.</summary>
[SparseFragmentModel]
public partial class PlaygroundRoster
{
    /// <summary>Gets or sets the quests.</summary>
    public List<PlaygroundQuest> Quests { get; set; } = new();
}

/// <summary>Source-generated metadata for trim-safe JSON in the playground.</summary>
[JsonSerializable(typeof(PlaygroundSettings))]
[JsonSerializable(typeof(PlaygroundNested))]
[JsonSerializable(typeof(PlaygroundQuest))]
[JsonSerializable(typeof(PlaygroundRoster))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(List<int>))]
internal sealed partial class PlaygroundJsonContext : JsonSerializerContext { }
