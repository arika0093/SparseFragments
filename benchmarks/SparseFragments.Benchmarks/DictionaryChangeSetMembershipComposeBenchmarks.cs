using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryChangeSetMembershipShape
{
    DisjointAdditions,
    AddRemove,
    RemoveRestore,
    AddEdit,
    RecreatedRestore,
    RecreatedEdit,
}

[MemoryDiagnoser]
public class DictionaryChangeSetMembershipComposeBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetMembershipShape Shape { get; set; }

    private BenchScalarDictHolder.ChangeSet _scalarFirst = null!;
    private BenchScalarDictHolder.ChangeSet _scalarSecond = null!;
    private BenchStructuralDictHolder.ChangeSet _structuralFirst = null!;
    private BenchStructuralDictHolder.ChangeSet _structuralSecond = null!;

    [GlobalSetup]
    public void Setup()
    {
        var scalarBefore = ScalarState(0);
        var scalarMiddle = ScalarState(1);
        var scalarAfter =
            Shape == DictionaryChangeSetMembershipShape.RemoveRestore
                ? scalarBefore
                : ScalarState(2);
        _scalarFirst = BenchScalarDictHolder.ChangeSet.Between(scalarBefore, scalarMiddle);
        _scalarSecond = BenchScalarDictHolder.ChangeSet.Between(scalarMiddle, scalarAfter);
        var structuralBefore = StructuralState(0);
        var structuralMiddle = StructuralState(1);
        var structuralAfter =
            Shape == DictionaryChangeSetMembershipShape.RemoveRestore
                ? structuralBefore
                : StructuralState(2);
        _structuralFirst = BenchStructuralDictHolder.ChangeSet.Between(
            structuralBefore,
            structuralMiddle
        );
        _structuralSecond = BenchStructuralDictHolder.ChangeSet.Between(
            structuralMiddle,
            structuralAfter
        );
        var scalar = ScalarCompose();
        var structural = StructuralCompose();
        var cancels =
            Shape
            is DictionaryChangeSetMembershipShape.AddRemove
                or DictionaryChangeSetMembershipShape.RemoveRestore
                or DictionaryChangeSetMembershipShape.RecreatedRestore;
        if (
            scalar.IsEmpty != cancels
            || structural.IsEmpty != cancels
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
                $"Dictionary membership composition failed: shape={Shape}, scalarEmpty={scalar.IsEmpty}, structuralEmpty={structural.IsEmpty}, scalarApply={BenchScalarDictHolder.Patch.Between(scalar.ToPatch().Apply(scalarBefore), scalarAfter).IsEmpty}, scalarInverse={BenchScalarDictHolder.Patch.Between(scalar.Invert().ToPatch().Apply(scalarAfter), scalarBefore).IsEmpty}, structuralApply={BenchStructuralDictHolder.Patch.Between(structural.ToPatch().Apply(structuralBefore), structuralAfter).IsEmpty}, structuralInverse={BenchStructuralDictHolder.Patch.Between(structural.Invert().ToPatch().Apply(structuralAfter), structuralBefore).IsEmpty}."
            );
    }

    private IEnumerable<int> Keys(int step) =>
        Enumerable
            .Range(0, Size)
            .Where(index =>
                Shape switch
                {
                    DictionaryChangeSetMembershipShape.DisjointAdditions => step == 2
                        || (step == 1 && index % 2 == 0),
                    DictionaryChangeSetMembershipShape.AddRemove => step == 1,
                    DictionaryChangeSetMembershipShape.RemoveRestore
                    or DictionaryChangeSetMembershipShape.RecreatedRestore
                    or DictionaryChangeSetMembershipShape.RecreatedEdit => step != 1,
                    _ => step != 0,
                }
            );

    private int Count(int index, int step) =>
        index
        + (
            Shape
                is DictionaryChangeSetMembershipShape.AddEdit
                    or DictionaryChangeSetMembershipShape.RecreatedEdit
            && step == 2
                ? 1
                : 0
        );

    private Optional<BenchScalarDictHolder.Fragment?> ScalarState(int step) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = Keys(step)
                        .ToDictionary(
                            index => "key-" + index,
                            index => Count(index, step),
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
                                Id = "srv-" + index,
                                Name = "server-" + index,
                                Count = Count(index, step),
                            },
                            StringComparer.Ordinal
                        ),
                }
            )
        );

    [Benchmark]
    public BenchScalarDictHolder.ChangeSet ScalarCompose() => _scalarFirst.Compose(_scalarSecond);

    [Benchmark]
    public BenchStructuralDictHolder.ChangeSet StructuralCompose() =>
        _structuralFirst.Compose(_structuralSecond);
}
