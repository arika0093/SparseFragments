using BenchmarkDotNet.Attributes;
using SparseTypes;

public enum ScalarCloneStorage
{
    Array,
    List,
    ReadOnlyCollection,
}

[MemoryDiagnoser]
public class ScalarCollectionCloneBenchmarks
{
    [Params(64, 8192)]
    public int Size { get; set; }

    [ParamsAllValues]
    public ScalarCloneStorage Storage { get; set; }

    private BenchAppendRecord _model = null!;

    [GlobalSetup]
    public void Setup()
    {
        var values = Enumerable.Range(0, Size).Select(index => index.ToString()).ToArray();
        _model = new BenchAppendRecord
        {
            Items = Storage switch
            {
                ScalarCloneStorage.Array => values,
                ScalarCloneStorage.List => values.ToList(),
                _ => System.Array.AsReadOnly(values),
            },
        };
        var clone = ModelClone();
        var fragment = FragmentFrom();
        values[0] = "mutated";
        if (
            ReferenceEquals(clone.Items, _model.Items)
            || ReferenceEquals(fragment.Items.Value, _model.Items)
            || clone.Items.Count != Size
            || fragment.Items.Value.Count != Size
            || clone.Items[0] != "0"
            || fragment.Items.Value[0] != "0"
        )
        {
            throw new InvalidOperationException(
                "Scalar collection snapshots must own their containers."
            );
        }
        values[0] = "0";
    }

    [Benchmark]
    public BenchAppendRecord ModelClone() => _model.DeepClone();

    [Benchmark]
    public BenchAppendRecord.Fragment FragmentFrom() => BenchAppendRecord.Fragment.From(_model);
}
