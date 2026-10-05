using System.Text.Json.Serialization;
using SparseFragments;

namespace SparseFragments.JsonPatch.Tests;

// Standalone RFC 6902 interop models. Configlue-side mirrors
// (ConfigluePatchWidget et al.) live in the Configlue repository's
// integration tests, not here.
[SparseFragmentModel]
public partial class PatchWidget
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public bool Enabled { get; set; } = true;

    public PatchNested? Nested { get; set; }

    public List<string> Tags { get; set; } = new();
}

[SparseFragmentModel]
public partial class PatchNested
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }
}

// Explicit wire names requiring escaping.
[SparseFragmentModel]
public partial class PatchNaming
{
    [JsonPropertyName("customName")]
    public string? Value { get; set; }

    [JsonPropertyName("a/b")]
    public int Slash { get; set; }

    [JsonPropertyName("m~n")]
    public int Tilde { get; set; }

    public int Plain { get; set; }
}

// Member names colliding with the bridge surface use the Sparse prefix.
[SparseFragmentModel]
public partial class PatchCollision
{
    public string? FromJsonPatch { get; set; }

    public string? ToJsonPatch { get; set; }

    public int Count { get; set; }
}
