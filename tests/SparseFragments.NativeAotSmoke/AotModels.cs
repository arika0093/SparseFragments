using System.Text.Json.Serialization;

namespace SparseFragments.NativeAotSmoke;

[SparseFragmentModel]
public partial class AotWidget
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public AotNested? Nested { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}

[SparseFragmentModel]
public partial class AotNested
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }
}

// Concrete collection shapes for the NativeAOT comparison regression
// coverage (#9): value-type sequence/set/dictionary members.
[SparseFragmentModel]
public partial class AotIntCollections
{
    public List<int> Numbers { get; set; } = [];

    public HashSet<int> Unique { get; set; } = [];

    public Dictionary<string, int> Scores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// Interface-typed variants of the same shapes.
[SparseFragmentModel]
public partial class AotInterfaceCollections
{
    public IReadOnlyList<int> Numbers { get; set; } = [];

    public ISet<int> Unique { get; set; } = new HashSet<int>();

    public IDictionary<string, int> Scores { get; set; } = new Dictionary<string, int>();
}

// Reference-type elements, including nullable members.
[SparseFragmentModel]
public partial class AotReferenceCollections
{
    public List<string?> Names { get; set; } = [];

    public HashSet<string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class AotSerializerContext : JsonSerializerContext { }
