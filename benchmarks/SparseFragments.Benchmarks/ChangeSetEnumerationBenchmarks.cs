using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum ChangeSetEnumerationShape
{
    NoOp,
    AllEdits,
    Mixed,
}

[MemoryDiagnoser]
public class ChangeSetEnumerationBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public ChangeSetEnumerationShape Shape { get; set; }

    private BenchScalarDictHolder.ChangeSet _scalar = null!;
    private BenchStructuralDictHolder.ChangeSet _structural = null!;

    [GlobalSetup]
    public void Setup()
    {
        var afterStep = Shape == ChangeSetEnumerationShape.NoOp ? 0 : 1;
        var scalarBefore = ScalarState(0);
        var scalarAfter = ScalarState(afterStep);
        var structuralBefore = StructuralState(0);
        var structuralAfter = StructuralState(afterStep);
        _scalar = BenchScalarDictHolder.ChangeSet.Between(scalarBefore, scalarAfter);
        _structural = BenchStructuralDictHolder.ChangeSet.Between(
            structuralBefore,
            structuralAfter
        );
        ValidateEntries(
            _scalar.EnumerateChanges().Select(change => (change.Path, change.Before, change.After)),
            _scalar
                .Invert()
                .EnumerateChanges()
                .Select(change => (change.Path, change.Before, change.After))
        );
        ValidateEntries(
            _structural
                .EnumerateChanges()
                .Select(change => (change.Path, change.Before, change.After)),
            _structural
                .Invert()
                .EnumerateChanges()
                .Select(change => (change.Path, change.Before, change.After))
        );
        if (
            _scalar.IsEmpty != (Shape == ChangeSetEnumerationShape.NoOp)
            || _structural.IsEmpty != (Shape == ChangeSetEnumerationShape.NoOp)
            || ScalarEnumerate() != ScalarEnumerate()
            || StructuralEnumerate() != StructuralEnumerate()
            || !BenchScalarDictHolder.Patch.Between(scalarBefore, ScalarState(0)).IsEmpty
            || !BenchScalarDictHolder.Patch.Between(scalarAfter, ScalarState(afterStep)).IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(structuralBefore, StructuralState(0))
                .IsEmpty
            || !BenchStructuralDictHolder
                .Patch.Between(structuralAfter, StructuralState(afterStep))
                .IsEmpty
        )
        {
            throw new InvalidOperationException(
                "Flattened enumeration must be repeatable and preserve source states."
            );
        }
    }

    private void ValidateEntries(
        IEnumerable<(string Path, Optional<object?> Before, Optional<object?> After)> forward,
        IEnumerable<(string Path, Optional<object?> Before, Optional<object?> After)> reverse
    )
    {
        var entries = forward.ToArray();
        var inverse = reverse.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var expected = Shape == ChangeSetEnumerationShape.NoOp ? 0 : Size;
        if (
            entries.Length != expected
            || inverse.Count != expected
            || entries.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count()
                != expected
        )
        {
            throw new InvalidOperationException(
                "Flattened dictionary changes must have one distinct path per changed key."
            );
        }
        foreach (var entry in entries)
        {
            if (
                string.IsNullOrEmpty(entry.Path)
                || !inverse.TryGetValue(entry.Path, out var opposite)
                || entry.Before.IsPresent != opposite.After.IsPresent
                || entry.After.IsPresent != opposite.Before.IsPresent
            )
            {
                throw new InvalidOperationException(
                    "Inversion must retain paths and swap endpoint presence."
                );
            }
            if (
                Shape == ChangeSetEnumerationShape.AllEdits
                && (
                    entry.Before.Value is not int before
                    || entry.After.Value is not int after
                    || after != before + 1
                    || !Equals(entry.Before.Value, opposite.After.Value)
                    || !Equals(entry.After.Value, opposite.Before.Value)
                )
            )
            {
                throw new InvalidOperationException(
                    "Flattened scalar and nested edits must retain their values."
                );
            }
        }
    }

    private IEnumerable<int> Keys(int step) =>
        Enumerable
            .Range(0, Size)
            .Where(index =>
                Shape != ChangeSetEnumerationShape.Mixed || index % 3 != (step == 0 ? 0 : 1)
            );

    private Optional<BenchScalarDictHolder.Fragment?> ScalarState(int step) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            BenchScalarDictHolder.Fragment.From(
                new()
                {
                    Scores = Keys(step)
                        .ToDictionary(index => "key-" + index, index => index + step),
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
                            }
                        ),
                }
            )
        );

    [Benchmark]
    public int ScalarEnumerate()
    {
        var checksum = 0;
        foreach (var change in _scalar.EnumerateChanges())
        {
            checksum +=
                change.Path.Length
                + (int)change.Kind
                + (change.Before.IsPresent ? 1 : 0)
                + (change.After.IsPresent ? 1 : 0);
        }
        return checksum;
    }

    [Benchmark]
    public int StructuralEnumerate()
    {
        var checksum = 0;
        foreach (var change in _structural.EnumerateChanges())
        {
            checksum +=
                change.Path.Length
                + (int)change.Kind
                + (change.Before.IsPresent ? 1 : 0)
                + (change.After.IsPresent ? 1 : 0);
        }
        return checksum;
    }
}
