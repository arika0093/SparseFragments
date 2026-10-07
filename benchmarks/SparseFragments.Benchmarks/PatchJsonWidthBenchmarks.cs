using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchPatchFourFields
{
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
}

[SparseFragmentModel]
public partial class BenchPatchEightFields
{
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
    public int E { get; set; }
    public int F { get; set; }
    public int G { get; set; }
    public int H { get; set; }
}

[MemoryDiagnoser]
public class PatchJsonWidthBenchmarks
{
    [Params(false, true)]
    public bool Dense { get; set; }

    private readonly JsonSerializerOptions _options = new()
    {
        TypeInfoResolver =
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };
    private byte[] _four = null!;
    private byte[] _eight = null!;

    [GlobalSetup]
    public void Setup()
    {
        var other = Dense ? 1 : 0;
        var fourBefore = Optional<BenchPatchFourFields.Fragment?>.Present(
            BenchPatchFourFields.Fragment.From(new())
        );
        var fourAfter = Optional<BenchPatchFourFields.Fragment?>.Present(
            BenchPatchFourFields.Fragment.From(
                new()
                {
                    A = other,
                    B = other,
                    C = other,
                    D = 1,
                }
            )
        );
        var eightBefore = Optional<BenchPatchEightFields.Fragment?>.Present(
            BenchPatchEightFields.Fragment.From(new())
        );
        var eightAfter = Optional<BenchPatchEightFields.Fragment?>.Present(
            BenchPatchEightFields.Fragment.From(
                new()
                {
                    A = other,
                    B = other,
                    C = other,
                    D = other,
                    E = other,
                    F = other,
                    G = other,
                    H = 1,
                }
            )
        );
        _four = JsonSerializer.SerializeToUtf8Bytes(
            BenchPatchFourFields.Patch.Between(fourBefore, fourAfter),
            _options
        );
        _eight = JsonSerializer.SerializeToUtf8Bytes(
            BenchPatchEightFields.Patch.Between(eightBefore, eightAfter),
            _options
        );
        if (
            !BenchPatchFourFields.Patch.Between(Four().Apply(fourBefore), fourAfter).IsEmpty
            || !BenchPatchEightFields.Patch.Between(Eight().Apply(eightBefore), eightAfter).IsEmpty
        )
            throw new InvalidOperationException(
                "Sparse and dense patch JSON must preserve values across model widths."
            );
        var unicodePatch = JsonSerializer.Deserialize<BenchPatchUnicodeFields.Patch>(
            "{\"日本語\":{\"kind\":\"set\",\"value\":1},\"__SparseJsonPropertyNames\":{\"kind\":\"set\",\"value\":2}}",
            _options
        )!;
        var unicodeResult = unicodePatch.Apply(
            Optional<BenchPatchUnicodeFields.Fragment?>.Present(
                BenchPatchUnicodeFields.Fragment.From(new())
            )
        );
        if (
            unicodeResult.Value!.日本語.Value != 1
            || unicodeResult.Value.__SparseJsonPropertyNames.Value != 2
        )
            throw new InvalidOperationException(
                "UTF-8 dispatch must preserve Unicode names and helper-name collisions."
            );
    }

    [Benchmark]
    public BenchPatchFourFields.Patch Four() =>
        JsonSerializer.Deserialize<BenchPatchFourFields.Patch>(_four, _options)!;

    [Benchmark]
    public BenchPatchEightFields.Patch Eight() =>
        JsonSerializer.Deserialize<BenchPatchEightFields.Patch>(_eight, _options)!;
}

[SparseFragmentModel]
public partial class BenchPatchUnicodeFields
{
    public int 日本語 { get; set; }
    public int __SparseJsonPropertyNames { get; set; }
}
