using BenchmarkDotNet.Attributes;
using SparseTypes;
using SparseTypes.Blazor;

// Repeated keyed-row resolution: one session.Field per row per field, as a
// dense form performs on every render. The setup pass builds the
// per-collection key index; measured passes are validated index hits, while
// the post-reorder benchmark measures the invalidation plus rebuild path.
[MemoryDiagnoser]
public class KeyedFieldResolutionBenchmarks
{
    [Params(10, 100, 1000, 10000)]
    public int Size { get; set; }

    private BenchFieldStateRoot _model = null!;
    private BenchFieldStateRoot.EditSession _session = null!;
    private string[] _keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        _model = new BenchFieldStateRoot { Title = "bench" };
        _keys = new string[Size];
        for (var i = 0; i < Size; i++)
        {
            var key = $"k-{i:00000}";
            _keys[i] = key;
            _model.Items.Add(
                new BenchFieldStateItem
                {
                    Id = key,
                    Name = $"n-{i}",
                    Count = i,
                }
            );
        }

        _session = _model.CreateEditSession();

        // Prime the index so measured passes reflect steady rerenders; the
        // post-reorder benchmark below measures the rebuild path instead.
        foreach (var key in _keys)
        {
            _session.Field($"Items[\"{key}\"].Name");
        }
    }

    [Benchmark(Description = "Field N keyed rows x1 field")]
    public int Field_AllRows_OneField()
    {
        var total = 0;
        foreach (var key in _keys)
        {
            total += _session.Field($"Items[\"{key}\"].Name").FieldName.Length;
        }

        return total;
    }

    [Benchmark(Description = "Field N keyed rows x3 fields")]
    public int Field_AllRows_ThreeFields()
    {
        var total = 0;
        foreach (var key in _keys)
        {
            total += _session.Field($"Items[\"{key}\"].Name").FieldName.Length;
            total += _session.Field($"Items[\"{key}\"].Count").FieldName.Length;
            total += _session.Field($"Items[\"{key}\"].Id").FieldName.Length;
        }

        return total;
    }

    [Benchmark(Description = "Field N keyed rows x3 fields after reorder")]
    public int Field_AllRows_AfterReorder()
    {
        _model.Items.Reverse();
        var total = 0;
        foreach (var key in _keys)
        {
            total += _session.Field($"Items[\"{key}\"].Name").FieldName.Length;
            total += _session.Field($"Items[\"{key}\"].Count").FieldName.Length;
            total += _session.Field($"Items[\"{key}\"].Id").FieldName.Length;
        }

        return total;
    }

    [Benchmark(Description = "Field one keyed row")]
    public string Field_SingleRow() => _session.Field($"Items[\"{_keys[0]}\"].Name").FieldName;
}
