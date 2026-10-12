using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

[MemoryDiagnoser]
public class KeyValidationStorageBenchmarks
{
    [Params(16, 2048, 16384)]
    public int Size { get; set; }

    [Benchmark(Baseline = true)]
    public int FreshSet() => Validate(new HashSet<int>(Size));

    [Benchmark]
    public int RentedSet()
    {
        using var scope = SparseFragmentRuntime.RentKeyValidation<int>(
            Size,
            static capacity => new HashSet<int>(capacity)
        );
        return Validate(scope.Keys);
    }

    private int Validate(HashSet<int> keys)
    {
        for (var i = 0; i < Size; i++)
        {
            if (!keys.Add(i))
                throw new InvalidOperationException("Duplicate key.");
        }
        return keys.Count;
    }
}
