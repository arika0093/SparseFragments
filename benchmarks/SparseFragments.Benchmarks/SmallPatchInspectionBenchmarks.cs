using BenchmarkDotNet.Attributes;
using SparseFragments;

[MemoryDiagnoser]
public class SmallPatchInspectionBenchmarks
{
    private BenchInspectionItem.Patch _empty = null!;
    private BenchInspectionItem.Patch _one = null!;
    private BenchInspectionItem.Patch _all = null!;
    private BenchInspectionItem.Patch _unset = null!;

    [GlobalSetup]
    public void Setup()
    {
        _empty = new();
        _one = new() { Score = 1 };
        _all = new() { Id = "changed", Score = 1 };
        _unset = new()
        {
            Id = FragmentOperation<string>.Unset,
            Score = FragmentOperation<int>.Unset,
        };
        var one = OneSet();
        var all = AllSet();
        var unset = AllUnset();
        if (
            Empty().Count != 0
            || one.Count != 1
            || one[0].Property.Name != "Score"
            || one[0].Kind != SparseChangeKind.Set
            || !Equals(one[0].Value, 1)
            || !all.Select(change => change.Property.Name).SequenceEqual(["Id", "Score"])
            || all.Any(change => change.Kind != SparseChangeKind.Set)
            || !Equals(all[0].Value, "changed")
            || !Equals(all[1].Value, 1)
            || !unset.Select(change => change.Property.Name).SequenceEqual(["Id", "Score"])
            || unset.Any(change =>
                change.Kind != SparseChangeKind.Unset || change.Value is not null
            )
            || all.Any(change =>
                !ReferenceEquals(
                    change.Property,
                    BenchInspectionItem.Sparse.Properties.Single(property =>
                        property.Name == change.Property.Name
                    )
                )
            )
        )
        {
            throw new InvalidOperationException(
                "Small model inspection produced unexpected changes."
            );
        }

        var snapshot = new BenchInspectionItem.Patch { Score = 1 };
        var original = snapshot.Changes;
        snapshot.Score = 2;
        if (!Equals(original.Single().Value, 1) || !Equals(snapshot.Changes.Single().Value, 2))
        {
            throw new InvalidOperationException("Inspection must retain independent snapshots.");
        }
    }

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> Empty() => _empty.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> OneSet() => _one.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> AllSet() => _all.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> AllUnset() => _unset.Changes;
}
