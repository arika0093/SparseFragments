using BenchmarkDotNet.Attributes;

public enum DictionaryLookupSequence
{
    RemovedRange,
    AlternatingMembership,
    MixedCycle,
    MissThenRemoved,
}

[MemoryDiagnoser]
public class DictionaryLookupSequenceBenchmarks
{
    [Params(2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Structural { get; set; }

    [ParamsAllValues]
    public DictionaryLookupSequence Sequence { get; set; }

    private DictionaryChangeSetBetweenBenchmarks _input = null!;
    private string[] _keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        _input = new()
        {
            Size = Size,
            Shape =
                Sequence == DictionaryLookupSequence.MixedCycle
                    ? DictionaryChangeSetBetweenShape.Mixed
                    : DictionaryChangeSetBetweenShape.ReplaceAll,
        };
        _input.Setup();
        _keys = Sequence switch
        {
            DictionaryLookupSequence.RemovedRange => Enumerable
                .Range(0, Size)
                .Select(static index => "key-" + index)
                .ToArray(),
            DictionaryLookupSequence.AlternatingMembership => Enumerable
                .Range(0, Size)
                .SelectMany(static index => new[] { "replacement-" + index, "key-" + index })
                .ToArray(),
            DictionaryLookupSequence.MixedCycle => Enumerable
                .Range(0, 128)
                .SelectMany(_ => new[] { "key-0", "key-1", "key-" + Size })
                .ToArray(),
            DictionaryLookupSequence.MissThenRemoved => ["unknown", "key-0"],
            _ => throw new InvalidOperationException("Unknown lookup sequence."),
        };
        var expected = Sequence == DictionaryLookupSequence.MissThenRemoved ? 1 : _keys.Length;
        if (LookupSequence() != expected)
            throw new InvalidOperationException("Lookup sequences must preserve item membership.");
    }

    [Benchmark]
    public int LookupSequence()
    {
        var count = 0;
        if (Structural)
        {
            var changes = _input.StructuralBetween();
            foreach (var key in _keys)
                count += changes.Servers.GetChange(key).IsChanged ? 1 : 0;
        }
        else
        {
            var changes = _input.ScalarBetween();
            foreach (var key in _keys)
                count += changes.Scores.GetChange(key).IsChanged ? 1 : 0;
        }
        return count;
    }
}
