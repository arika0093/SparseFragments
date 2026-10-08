using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryChangeSetWholeComposeOperation
{
    Add,
    Remove,
    Edit,
}

[MemoryDiagnoser]
public class DictionaryChangeSetWholeComposeBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetWholeComposeOperation Operation { get; set; }

    [Params(false, true)]
    public bool WholeFirst { get; set; }

    private BenchScalarDictHolder.ChangeSet _first = null!;
    private BenchScalarDictHolder.ChangeSet _second = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Fragment(0);
        var after = Fragment(1);
        var sparse = BenchScalarDictHolder.ChangeSet.Between(before, after);
        var missing = Optional<BenchScalarDictHolder.Fragment?>.Missing;
        var intermediate = Fragment(WholeFirst ? 0 : 1);
        var original = intermediate.Value!.Scores.Value!.ToArray();
        var whole = WholeFirst
            ? BenchScalarDictHolder.ChangeSet.Between(missing, intermediate)
            : BenchScalarDictHolder.ChangeSet.Between(intermediate, missing);
        _first = WholeFirst ? whole : sparse;
        _second = WholeFirst ? sparse : whole;
        var initial = WholeFirst ? missing : before;
        var expected = WholeFirst ? after : missing;
        var composed = Compose();
        var applied = composed.ToPatch().Apply(initial);
        if (
            sparse.IsEmpty
            || whole.IsEmpty
            || composed.IsEmpty
            || !BenchScalarDictHolder.Patch.Between(applied, expected).IsEmpty
            || !BenchScalarDictHolder
                .Patch.Between(composed.Invert().ToPatch().Apply(applied), initial)
                .IsEmpty
            || !intermediate.Value.Scores.Value!.SequenceEqual(original)
            || !BenchScalarDictHolder.Patch.Between(before, Fragment(0)).IsEmpty
            || !BenchScalarDictHolder.Patch.Between(after, Fragment(1)).IsEmpty
        )
        {
            throw new InvalidOperationException(
                "Whole and sparse dictionary composition must preserve endpoints, inversion, and sources."
            );
        }

        if (original.Length != 0)
        {
            var invalid = Fragment(WholeFirst ? 0 : 1);
            invalid.Value!.Scores.Value!["key-" + (Size - 1)] += 10;
            var badWhole = WholeFirst
                ? BenchScalarDictHolder.ChangeSet.Between(missing, invalid)
                : BenchScalarDictHolder.ChangeSet.Between(invalid, missing);
            RequireRejection(WholeFirst ? badWhole : sparse, WholeFirst ? sparse : badWhole);
        }
        ValidateCustomEquality();
    }

    private void ValidateCustomEquality()
    {
        var before = CustomFragment(1);
        var after = CustomFragment(2);
        var sparse = BenchEqualityDictHolder.ChangeSet.Between(before, after);
        var invalid = CustomFragment(WholeFirst ? 11 : 12);
        var missing = Optional<BenchEqualityDictHolder.Fragment?>.Missing;
        var badWhole = WholeFirst
            ? BenchEqualityDictHolder.ChangeSet.Between(missing, invalid)
            : BenchEqualityDictHolder.ChangeSet.Between(invalid, missing);
        try
        {
            _ = WholeFirst ? badWhole.Compose(sparse) : sparse.Compose(badWhole);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Custom struct continuity must use object equality.");
    }

    private static void RequireRejection(
        BenchScalarDictHolder.ChangeSet first,
        BenchScalarDictHolder.ChangeSet second
    )
    {
        try
        {
            _ = first.Compose(second);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            "Changed dictionary values must reject noncontiguous endpoints."
        );
    }

    private static Optional<BenchEqualityDictHolder.Fragment?> CustomFragment(int value) =>
        Optional<BenchEqualityDictHolder.Fragment?>.Present(
            BenchEqualityDictHolder.Fragment.From(
                new() { CustomValues = new() { ["key"] = new BenchCustomScalar(value) } }
            )
        );

    private Optional<BenchScalarDictHolder.Fragment?> Fragment(int step)
    {
        var empty =
            (Operation == DictionaryChangeSetWholeComposeOperation.Add && step == 0)
            || (Operation == DictionaryChangeSetWholeComposeOperation.Remove && step == 1);
        var values = new Dictionary<string, int>(empty ? 0 : Size, StringComparer.Ordinal);
        if (!empty)
        {
            for (var index = 0; index < Size; index++)
            {
                values.Add("key-" + index, index + step);
            }
        }
        return Optional<BenchScalarDictHolder.Fragment?>.Present(
            new() { Scores = Optional<Dictionary<string, int>>.Present(values) }
        );
    }

    [Benchmark]
    public BenchScalarDictHolder.ChangeSet Compose() => _first.Compose(_second);
}
