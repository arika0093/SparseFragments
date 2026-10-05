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

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class AotSerializerContext : JsonSerializerContext { }
