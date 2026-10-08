using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryChangeSetBetweenShape
{
    NoOp,
    OneEdit,
    AllEdits,
}

[MemoryDiagnoser]
public class DictionaryChangeSetBetweenBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetBetweenShape Shape { get; set; }

    private Optional<BenchScalarDictHolder.Fragment?> _scalarBefore;
    private Optional<BenchScalarDictHolder.Fragment?> _scalarAfter;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralBefore;
    private Optional<BenchStructuralDictHolder.Fragment?> _structuralAfter;

    [GlobalSetup]
    public void Setup()
    {
        _scalarBefore = ScalarState(false);
        _scalarAfter = ScalarState(true);
        _structuralBefore = StructuralState(false);
        _structuralAfter = StructuralState(true);
        var scalar = ScalarBetween();
        var structural = StructuralBetween();
        var empty = Shape == DictionaryChangeSetBetweenShape.NoOp;
        if (
            scalar.IsEmpty != empty
            || structural.IsEmpty != empty
            || !BenchScalarDictHolder
                .Patch.Between(scalar.ToPatch().Apply(_scalarBefore), _scalarAfter)
                .IsEmpty
            || !BenchScalarDictHolder
                .Patch.Between(scalar.Invert().ToPatch().Apply(_scalarAfter), _scalarBefore)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(structural.ToPatch().Apply(_structuralBefore), _structuralAfter)
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(
                    structural.Invert().ToPatch().Apply(_structuralAfter),
                    _structuralBefore
                )
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Dictionary differences must preserve endpoints and invert correctly."
            );
    }

    private int Value(int index, bool after) =>
        index
        + (
            after
            && (
                Shape == DictionaryChangeSetBetweenShape.AllEdits
                || (Shape == DictionaryChangeSetBetweenShape.OneEdit && index == 0)
            )
                ? 1
                : 0
        );

    private Optional<BenchScalarDictHolder.Fragment?> ScalarState(bool after) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = Enumerable
                        .Range(0, Size)
                        .ToDictionary(
                            index => "key-" + index,
                            index => Value(index, after),
                            StringComparer.Ordinal
                        ),
                }
            )
        );

    private Optional<BenchStructuralDictHolder.Fragment?> StructuralState(bool after) =>
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
                                Count = Value(index, after),
                            },
                            StringComparer.Ordinal
                        ),
                }
            )
        );

    [Benchmark]
    public BenchScalarDictHolder.ChangeSet ScalarBetween() =>
        BenchScalarDictHolder.ChangeSet.Between(_scalarBefore, _scalarAfter);

    [Benchmark]
    public BenchStructuralDictHolder.ChangeSet StructuralBetween() =>
        BenchStructuralDictHolder.ChangeSet.Between(_structuralBefore, _structuralAfter);
}
