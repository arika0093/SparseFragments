using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchSessionPathsItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchSessionPathsCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchSessionPathsOrder
{
    public BenchSessionPathsCustomer Customer { get; set; } = new();

    public List<BenchSessionPathsItem> Items { get; set; } = new();
}

// Session-owned path-navigation budgets (issue #270). The root builder is
// cached per session, so repeated Paths access copies a small struct without
// heap churn; element navigation allocates only the segments it appends,
// exactly like the model root builder.
[MemoryDiagnoser]
public class SessionPathsBenchmarks
{
    private BenchSessionPathsOrder.EditSession _session = null!;

    [GlobalSetup]
    public void Setup()
    {
        _session = new BenchSessionPathsOrder
        {
            Customer = new BenchSessionPathsCustomer { Name = "name" },
            Items = Enumerable
                .Range(0, 64)
                .Select(index => new BenchSessionPathsItem
                {
                    Id = "k-" + index,
                    Title = "t-" + index,
                })
                .ToList(),
        }.CreateEditSession();
        if (
            _session.Paths.Customer.Name.ToString() != "Customer.Name"
            || _session.Paths.Items.Key("k-1").Title.ToString() != "Items[\"k-1\"].Title"
        )
        {
            throw new InvalidOperationException(
                "Session path fixtures must resolve through the session navigation surface."
            );
        }
    }

    [Benchmark(Description = "Session Paths root access")]
    public string Session_Paths_Root() => _session.Paths.UntypedPath.ToString();

    [Benchmark(Description = "Session nested leaf (2 segments)")]
    public string Session_Nested_Leaf() => _session.Paths.Customer.Name.ToString();

    [Benchmark(Description = "Session keyed leaf (key + member)")]
    public string Session_Keyed_Leaf() => _session.Paths.Items.Key("k-1").Title.ToString();
}
