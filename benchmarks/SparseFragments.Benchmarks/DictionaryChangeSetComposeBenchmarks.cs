using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryChangeSetComposeShape
{
    Disjoint,
    Overlap,
    Cancellation,
}

[MemoryDiagnoser]
public class DictionaryChangeSetComposeBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetComposeShape Shape { get; set; }

    private BenchScalarDictHolder.ChangeSet _scalarFirst = null!;
    private BenchScalarDictHolder.ChangeSet _scalarSecond = null!;
    private BenchStructuralDictHolder.ChangeSet _structuralFirst = null!;
    private BenchStructuralDictHolder.ChangeSet _structuralSecond = null!;

    [GlobalSetup]
    public void Setup()
    {
        var scalarBefore = ScalarState(0);
        var scalarAfter = ScalarState(2);
        _scalarFirst = BenchScalarDictHolder.ChangeSet.Between(scalarBefore, ScalarState(1));
        _scalarSecond = BenchScalarDictHolder.ChangeSet.Between(ScalarState(1), scalarAfter);
        var scalar = ScalarCompose();
        var structuralBefore = StructuralState(0);
        var structuralAfter = StructuralState(2);
        _structuralFirst = BenchStructuralDictHolder.ChangeSet.Between(
            structuralBefore,
            StructuralState(1)
        );
        _structuralSecond = BenchStructuralDictHolder.ChangeSet.Between(
            StructuralState(1),
            structuralAfter
        );
        var structural = StructuralCompose();
        if (
            scalar.IsEmpty != (Shape == DictionaryChangeSetComposeShape.Cancellation)
            || structural.IsEmpty != (Shape == DictionaryChangeSetComposeShape.Cancellation)
            || !BenchScalarDictHolder
                .Patch.Between(scalar.ToPatch().Apply(scalarBefore), scalarAfter)
                .IsEmpty
            || !BenchScalarDictHolder
                .Patch.Between(scalar.Invert().ToPatch().Apply(scalarAfter), scalarBefore)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(structural.ToPatch().Apply(structuralBefore), structuralAfter)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(
                    structural.Invert().ToPatch().Apply(structuralAfter),
                    structuralBefore
                )
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Dictionary compositions must preserve endpoints, invert correctly, and cancel restored changes."
            );
    }

    private Optional<BenchScalarDictHolder.Fragment?> ScalarState(int step) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = Enumerable
                        .Range(0, Size)
                        .ToDictionary(
                            index => "key-" + index,
                            index => index + Increment(index, step),
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
                    Servers = Enumerable
                        .Range(0, Size)
                        .ToDictionary(
                            index => "key-" + index,
                            index => new BenchKeyedServer
                            {
                                Id = "srv-" + index,
                                Name = "server-" + index,
                                Count = index + Increment(index, step),
                            },
                            StringComparer.Ordinal
                        ),
                }
            )
        );

    private int Increment(int index, int step) =>
        Shape switch
        {
            DictionaryChangeSetComposeShape.Disjoint => index % 2 == 0
                ? (step > 0 ? 1 : 0)
                : (step == 2 ? 1 : 0),
            DictionaryChangeSetComposeShape.Overlap => step,
            _ => step == 1 ? 1 : 0,
        };

    [Benchmark]
    public BenchScalarDictHolder.ChangeSet ScalarCompose() => _scalarFirst.Compose(_scalarSecond);

    [Benchmark]
    public BenchStructuralDictHolder.ChangeSet StructuralCompose() =>
        _structuralFirst.Compose(_structuralSecond);
}
