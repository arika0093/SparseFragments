using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum ChangeSetLifecycleShape
{
    Empty,
    Scalar,
    Collection,
}

[MemoryDiagnoser]
public class ChangeSetLifecycleBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        ChangeSetLifecycleShape.Empty,
        ChangeSetLifecycleShape.Scalar,
        ChangeSetLifecycleShape.Collection
    )]
    public ChangeSetLifecycleShape Shape { get; set; }

    private Optional<BenchChangeSetRebaseRecord.Fragment?> _before;
    private Optional<BenchChangeSetRebaseRecord.Fragment?> _after;
    private Optional<BenchChangeSetRebaseRecord.Fragment?> _final;
    private BenchChangeSetRebaseRecord.ChangeSet _change = null!;
    private BenchChangeSetRebaseRecord.ChangeSet _next = null!;

    [GlobalSetup]
    public void Setup()
    {
        _before = State(0);
        _after = State(Shape == ChangeSetLifecycleShape.Empty ? 0 : 1);
        _final = State(Shape == ChangeSetLifecycleShape.Empty ? 0 : 2);
        _change = Between();
        // A distinct but equivalent intermediate state exercises semantic contiguity.
        _next = BenchChangeSetRebaseRecord.ChangeSet.Between(
            State(Shape == ChangeSetLifecycleShape.Empty ? 0 : 1),
            _final
        );
        Validate(ToPatch().Apply(_before), _after);
        Validate(Invert().ToPatch().Apply(_after), _before);
        Validate(Compose().ToPatch().Apply(_before), _final);
        if (_change.IsEmpty != (Shape == ChangeSetLifecycleShape.Empty))
            throw new InvalidOperationException("ChangeSet emptiness must match the transition.");
        if (ReferenceEquals(ToPatch(), ToPatch()))
            throw new InvalidOperationException("ToPatch must return independent patch instances.");
    }

    private Optional<BenchChangeSetRebaseRecord.Fragment?> State(int step) =>
        Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(
            BenchChangeSetRebaseRecord.Fragment.From(
                new()
                {
                    Label = "base",
                    Counter = step,
                    Values = Enumerable
                        .Range(0, Size)
                        .Select(value =>
                            Shape == ChangeSetLifecycleShape.Collection ? value + step : value
                        )
                        .ToList(),
                }
            )
        );

    private static void Validate(
        Optional<BenchChangeSetRebaseRecord.Fragment?> actual,
        Optional<BenchChangeSetRebaseRecord.Fragment?> expected
    )
    {
        if (
            !actual.IsPresent
            || actual.Value is null
            || actual.Value.Counter != expected.Value!.Counter
            || actual.Value.Label != expected.Value.Label
            || actual
                .Value.Values.GetValueOrDefault()
                ?.SequenceEqual(expected.Value.Values.GetValueOrDefault()!) != true
        )
            throw new InvalidOperationException(
                "ChangeSet operations must preserve the expected state."
            );
    }

    [Benchmark]
    public BenchChangeSetRebaseRecord.ChangeSet Between() =>
        BenchChangeSetRebaseRecord.ChangeSet.Between(_before, _after);

    [Benchmark]
    public BenchChangeSetRebaseRecord.Patch ToPatch() => _change.ToPatch();

    [Benchmark]
    public BenchChangeSetRebaseRecord.ChangeSet Invert() => _change.Invert();

    [Benchmark]
    public BenchChangeSetRebaseRecord.ChangeSet Compose() => _change.Compose(_next);
}
