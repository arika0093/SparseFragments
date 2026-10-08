using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryChangeSetProjectionOperation
{
    Add,
    Remove,
    Edit,
    Mixed,
}

[MemoryDiagnoser]
public class DictionaryChangeSetToPatchBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetProjectionOperation Operation { get; set; }

    private BenchScalarDictHolder.ChangeSet _scalar = null!;
    private BenchStructuralDictHolder.ChangeSet _structural = null!;

    [GlobalSetup]
    public void Setup()
    {
        var scalarBefore = ScalarState(0);
        var scalarAfter = ScalarState(1);
        var structuralBefore = StructuralState(0);
        var structuralAfter = StructuralState(1);
        _scalar = BenchScalarDictHolder.ChangeSet.Between(scalarBefore, scalarAfter);
        _structural = BenchStructuralDictHolder.ChangeSet.Between(
            structuralBefore,
            structuralAfter
        );
        if (
            _scalar.IsEmpty
            || _structural.IsEmpty
            || !BenchScalarDictHolder
                .Patch.Between(ScalarToPatch().Apply(scalarBefore), scalarAfter)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(StructuralToPatch().Apply(structuralBefore), structuralAfter)
                .IsEmpty
            || !BenchScalarDictHolder
                .Patch.Between(_scalar.Invert().ToPatch().Apply(scalarAfter), scalarBefore)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(
                    _structural.Invert().ToPatch().Apply(structuralAfter),
                    structuralBefore
                )
                .IsEmpty
            || !BenchScalarDictHolder.Patch.Between(scalarBefore, ScalarState(0)).IsEmpty
            || !BenchScalarDictHolder.Patch.Between(scalarAfter, ScalarState(1)).IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(structuralBefore, StructuralState(0))
                .IsEmpty
            || !BenchStructuralDictHolder.Patch.Between(structuralAfter, StructuralState(1)).IsEmpty
            || ReferenceEquals(ScalarToPatch(), ScalarToPatch())
            || ReferenceEquals(StructuralToPatch(), StructuralToPatch())
        )
        {
            throw new InvalidOperationException(
                "Dictionary patch projection must preserve endpoints, inversion, independent patches, and source values."
            );
        }
    }

    private bool IsEmptyStep(int step) =>
        (Operation == DictionaryChangeSetProjectionOperation.Add && step == 0)
        || (Operation == DictionaryChangeSetProjectionOperation.Remove && step == 1);

    private IEnumerable<int> Keys(int step) =>
        Operation == DictionaryChangeSetProjectionOperation.Mixed
            ? Enumerable.Range(0, Size).Where(index => index % 3 != (step == 0 ? 0 : 1))
            : Enumerable.Range(0, IsEmptyStep(step) ? 0 : Size);

    private Optional<BenchScalarDictHolder.Fragment?> ScalarState(int step) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = Keys(step)
                        .ToDictionary(
                            index => "key-" + index,
                            index => index + step,
                            StringComparer.Ordinal
                        ),
                }
            )
        );

    private Optional<BenchStructuralDictHolder.Fragment?> StructuralState(int step) =>
        Optional<BenchStructuralDictHolder.Fragment?>.Present(
            BenchStructuralDictHolder.Fragment.From(
                new()
                {
                    Servers = Keys(step)
                        .ToDictionary(
                            index => "key-" + index,
                            index => new BenchKeyedServer
                            {
                                Id = "server-" + index,
                                Name = "server",
                                Count = index + step,
                            },
                            StringComparer.Ordinal
                        ),
                }
            )
        );

    [Benchmark]
    public BenchScalarDictHolder.Patch ScalarToPatch() => _scalar.ToPatch();

    [Benchmark]
    public BenchStructuralDictHolder.Patch StructuralToPatch() => _structural.ToPatch();
}
