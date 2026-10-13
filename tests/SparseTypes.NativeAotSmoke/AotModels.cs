using System.Text.Json.Serialization;
using SparseTypes.NativeAotFixtures;

namespace SparseTypes.NativeAotSmoke;

[SparseModel]
public partial class AotWidget
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public AotNested? Nested { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}

[SparseModel]
public partial class AotNested
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }
}

// Concrete collection shapes for the NativeAOT comparison regression
// coverage (#9): value-type sequence/set/dictionary members.
[SparseModel]
public partial class AotIntCollections
{
    public List<int> Numbers { get; set; } = [];

    public HashSet<int> Unique { get; set; } = [];

    public Dictionary<string, int> Scores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// Interface-typed variants of the same shapes.
[SparseModel]
public partial class AotInterfaceCollections
{
    public IReadOnlyList<int> Numbers { get; set; } = [];

    public ISet<int> Unique { get; set; } = new HashSet<int>();

    public IDictionary<string, int> Scores { get; set; } = new Dictionary<string, int>();
}

// Reference-type elements, including nullable members.
[SparseModel]
public partial class AotReferenceCollections
{
    public List<string?> Names { get; set; } = [];

    public HashSet<string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[SparseModel]
public partial class AotServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseModel]
public partial class AotServerHolder
{
    public List<AotServer> Items { get; set; } = new();
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(AotOrderStatus))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(AotServer))]
[JsonSerializable(typeof(List<AotServer>))]
[JsonSerializable(typeof(PayloadRoot.ChangePayload), TypeInfoPropertyName = "PayloadRootChangePayload")]
[JsonSerializable(typeof(PayloadCollection.ChangePayload), TypeInfoPropertyName = "PayloadCollectionChangePayload")]
[JsonSerializable(typeof(PayloadSecret.ChangePayload), TypeInfoPropertyName = "PayloadSecretChangePayload")]
internal sealed partial class AotSerializerContext : JsonSerializerContext { }
