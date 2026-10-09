using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchDescriptorChild
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class BenchDescriptorHolder
{
    public string Title { get; set; } = string.Empty;

    public List<BenchDescriptorChild> Items { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } = new();
}

// Quantifies session descriptor-graph caching (issue #134): repeated root
// lookups must reuse one graph instead of reallocating it per access.
[MemoryDiagnoser]
public class DescriptorAccessBenchmarks
{
    [Params(16, 512)]
    public int Size { get; set; }

    private const int Reads = 100;

    private BenchDescriptorHolder.EditSession _session = null!;

    [GlobalSetup]
    public void Setup()
    {
        var model = new BenchDescriptorHolder
        {
            Title = "holder",
            Items = Enumerable
                .Range(0, Size)
                .Select(index => new BenchDescriptorChild
                {
                    Id = "id-" + index,
                    Name = "n-" + index,
                })
                .ToList(),
            Metadata = Enumerable
                .Range(0, Size)
                .ToDictionary(index => "k-" + index, index => "v-" + index),
        };
        _session = model.CreateEditSession();
        if (!_session.Descriptors.TryGet(nameof(BenchDescriptorHolder.Title), out _))
            throw new InvalidOperationException("Descriptors must resolve the title member.");
    }

    [Benchmark(Description = "100 repeated root descriptor lookups")]
    public int RootRepeatedAccess()
    {
        var count = 0;
        for (var index = 0; index < Reads; index++)
        {
            count += _session.Descriptors.Members.Count;
        }

        return count;
    }

    [Benchmark(Description = "100 repeated nested item descriptor lookups")]
    public int NestedRepeatedAccess()
    {
        var length = 0;
        for (var index = 0; index < Reads; index++)
        {
            _session.Descriptors.TryGet(nameof(BenchDescriptorHolder.Items), out var items);
            var nested = items.Array!.GetItemDescriptors(index % Size)!;
            nested.TryGet(nameof(BenchDescriptorChild.Name), out var name);
            length += ((string)name.GetValue()!).Length;
        }

        return length;
    }
}
