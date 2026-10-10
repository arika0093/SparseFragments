using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchPayloadWide
{
    public int Value00 { get; set; }

    public int Value01 { get; set; }

    public int Value02 { get; set; }

    public int Value03 { get; set; }

    public int Value04 { get; set; }

    public int Value05 { get; set; }

    public int Value06 { get; set; }

    public int Value07 { get; set; }

    public int Value08 { get; set; }

    public int Value09 { get; set; }

    public int Value10 { get; set; }

    public int Value11 { get; set; }

    public int Value12 { get; set; }

    public int Value13 { get; set; }

    public int Value14 { get; set; }

    public int Value15 { get; set; }

    public int Value16 { get; set; }

    public int Value17 { get; set; }

    public int Value18 { get; set; }

    public int Value19 { get; set; }

    public int Value20 { get; set; }

    public int Value21 { get; set; }

    public int Value22 { get; set; }

    public int Value23 { get; set; }

    public int Value24 { get; set; }

    public int Value25 { get; set; }

    public int Value26 { get; set; }

    public int Value27 { get; set; }

    public int Value28 { get; set; }

    public int Value29 { get; set; }

    public int Value30 { get; set; }

    public int Value31 { get; set; }

    public int Value32 { get; set; }

    public int Value33 { get; set; }

    public int Value34 { get; set; }

    public int Value35 { get; set; }

    public int Value36 { get; set; }

    public int Value37 { get; set; }

    public int Value38 { get; set; }

    public int Value39 { get; set; }

    public int Value40 { get; set; }

    public int Value41 { get; set; }

    public int Value42 { get; set; }

    public int Value43 { get; set; }

    public int Value44 { get; set; }

    public int Value45 { get; set; }

    public int Value46 { get; set; }

    public int Value47 { get; set; }

    public int Value48 { get; set; }

    public int Value49 { get; set; }

    public int Value50 { get; set; }

    public int Value51 { get; set; }

    public int Value52 { get; set; }

    public int Value53 { get; set; }

    public int Value54 { get; set; }

    public int Value55 { get; set; }

    public int Value56 { get; set; }

    public int Value57 { get; set; }

    public int Value58 { get; set; }

    public int Value59 { get; set; }

    public int Value60 { get; set; }

    public int Value61 { get; set; }

    public int Value62 { get; set; }

    public int Value63 { get; set; }
}

[MemoryDiagnoser]
public class WideChangePayloadConstructionBenchmarks
{
    [Params(0, 1, 64)]
    public int Changes { get; set; }

    private BenchPayloadWide.ChangeSet _change = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Optional<BenchPayloadWide.Fragment?>.Present(
            BenchPayloadWide.Fragment.From(new())
        );
        var model = new BenchPayloadWide();
        for (var index = 0; index < Changes; index++)
            typeof(BenchPayloadWide)
                .GetProperty("Value" + index.ToString("D2", CultureInfo.InvariantCulture))!
                .SetValue(model, 1);
        var after = Optional<BenchPayloadWide.Fragment?>.Present(
            BenchPayloadWide.Fragment.From(model)
        );
        _change = BenchPayloadWide.ChangeSet.Between(before, after);
        var restored = JsonSerializer
            .Deserialize<BenchPayloadWide.ChangePayload>(
                JsonSerializer.SerializeToUtf8Bytes(Payload())
            )!
            .ToChangeSet();
        if (
            !BenchPayloadWide.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty
            || _change.IsEmpty != (Changes == 0)
        )
            throw new InvalidOperationException(
                "Wide payloads must retain empty, sparse and dense transitions."
            );
    }

    [Benchmark]
    public BenchPayloadWide.ChangePayload Payload() => _change.ToPayload();
}
