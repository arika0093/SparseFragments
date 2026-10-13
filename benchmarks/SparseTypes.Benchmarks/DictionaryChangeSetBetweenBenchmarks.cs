using BenchmarkDotNet.Attributes;
using SparseTypes;

public enum DictionaryChangeSetBetweenShape
{
    NoOp,
    OneEdit,
    AllEdits,
    AddLast,
    RemoveFirst,
    ReplaceAll,
    Mixed,
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
        var added =
            Shape == DictionaryChangeSetBetweenShape.ReplaceAll ? Size
            : Shape
                is DictionaryChangeSetBetweenShape.AddLast
                    or DictionaryChangeSetBetweenShape.Mixed
                ? 1
            : 0;
        var removed =
            Shape == DictionaryChangeSetBetweenShape.ReplaceAll ? Size
            : Shape
                is DictionaryChangeSetBetweenShape.RemoveFirst
                    or DictionaryChangeSetBetweenShape.Mixed
                ? 1
            : 0;
        var edited =
            Shape == DictionaryChangeSetBetweenShape.AllEdits ? Size
            : Shape
                is DictionaryChangeSetBetweenShape.OneEdit
                    or DictionaryChangeSetBetweenShape.Mixed
                ? 1
            : 0;
        if (
            scalar.IsEmpty != empty
            || structural.IsEmpty != empty
            || scalar.Scores.Added.Count != added
            || scalar.Scores.Removed.Count != removed
            || scalar.Scores.Edited.Count != edited
            || structural.Servers.Added.Count != added
            || structural.Servers.Removed.Count != removed
            || structural.Servers.Edited.Count != edited
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
                || (Shape == DictionaryChangeSetBetweenShape.Mixed && index == 1)
            )
                ? 1
                : 0
        );

    private Optional<BenchScalarDictHolder.Fragment?> ScalarState(bool after) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = Indices(after)
                        .ToDictionary(
                            index => Key(index, after),
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
                    Servers = Indices(after)
                        .ToDictionary(
                            index => Key(index, after),
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

    private IEnumerable<int> Indices(bool after)
    {
        var start =
            after
            && Shape
                is DictionaryChangeSetBetweenShape.RemoveFirst
                    or DictionaryChangeSetBetweenShape.Mixed
                ? 1
                : 0;
        var end =
            Size
            + (
                after
                && Shape
                    is DictionaryChangeSetBetweenShape.AddLast
                        or DictionaryChangeSetBetweenShape.Mixed
                    ? 1
                    : 0
            );
        return Enumerable.Range(start, end - start);
    }

    private string Key(int index, bool after) =>
        (after && Shape == DictionaryChangeSetBetweenShape.ReplaceAll ? "replacement-" : "key-")
        + index;

    public static void ValidateInputs()
    {
        foreach (var size in new[] { 16, 2048 })
        foreach (var shape in Enum.GetValues<DictionaryChangeSetBetweenShape>())
            new DictionaryChangeSetBetweenBenchmarks { Size = size, Shape = shape }.Setup();
    }

    [Benchmark]
    public BenchScalarDictHolder.ChangeSet ScalarBetween() =>
        BenchScalarDictHolder.ChangeSet.Between(_scalarBefore, _scalarAfter);

    [Benchmark]
    public BenchStructuralDictHolder.ChangeSet StructuralBetween() =>
        BenchStructuralDictHolder.ChangeSet.Between(_structuralBefore, _structuralAfter);
}
