using BenchmarkDotNet.Attributes;
using SparseTypes;
using SparseTypes.Generated;

[MemoryDiagnoser]
public class ChangeRecordStorageBenchmarks
{
    [Params(16, 2048)]
    public int Count { get; set; }

    [Params(false, true)]
    public bool Preallocate { get; set; }

    private readonly SparsePath _path = SparsePath.Root<object>();
    private readonly Optional<object?> _before = Optional<object?>.Present(1);
    private readonly Optional<object?> _after = Optional<object?>.Present(2);

    [GlobalSetup]
    public void Setup()
    {
        var classes = (List<SparseChangeRecord>)ClassRecords();
        var values = (List<ValueRecord>)StructRecords();
        if (classes.Count != Count || values.Count != Count)
            throw new InvalidOperationException("Record storage must retain every entry.");
        for (var index = 0; index < Count; index++)
        {
            if (
                !classes[index].Path.Equals(values[index].Path)
                || !classes[index].Before.Equals(values[index].Before)
                || !classes[index].After.Equals(values[index].After)
                || classes[index].Kind != values[index].Kind
            )
                throw new InvalidOperationException(
                    "Record representations must retain the same data."
                );
        }
    }

    [Benchmark(Baseline = true)]
    public object ClassRecords()
    {
        var records = new List<SparseChangeRecord>(Preallocate ? Count : 0);
        for (var index = 0; index < Count; index++)
            records.Add(new SparseChangeRecord(_path, _before, _after, SparseChangeKind.Changed));
        return records;
    }

    [Benchmark]
    public object StructRecords()
    {
        var records = new List<ValueRecord>(Preallocate ? Count : 0);
        for (var index = 0; index < Count; index++)
            records.Add(
                new ValueRecord
                {
                    Path = _path,
                    Before = _before,
                    After = _after,
                    Kind = SparseChangeKind.Changed,
                }
            );
        return records;
    }

    private readonly struct ValueRecord
    {
        public SparsePath Path { get; init; }
        public Optional<object?> Before { get; init; }
        public Optional<object?> After { get; init; }
        public SparseChangeKind Kind { get; init; }
    }
}
