using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class ChangeSetWideEnumerationBenchmarks
{
    private BenchPathWide.ChangeSet _changes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var after = new BenchPathWide
        {
            Value00 = 1,
            Value01 = 1,
            Value02 = 1,
            Value03 = 1,
            Value04 = 1,
            Value05 = 1,
            Value06 = 1,
            Value07 = 1,
            Value08 = 1,
            Value09 = 1,
            Value10 = 1,
            Value11 = 1,
            Value12 = 1,
            Value13 = 1,
            Value14 = 1,
            Value15 = 1,
        };
        _changes = BenchPathWide.ChangeSet.Between(new BenchPathWide(), after);
        if (Values() != 16)
        {
            throw new InvalidOperationException("Every scalar transition must be enumerated.");
        }
    }

    [Benchmark]
    public int Values()
    {
        var checksum = 0;
        foreach (var change in _changes.EnumerateChanges())
        {
            checksum += (int)change.After.Value!;
        }
        return checksum;
    }
}
