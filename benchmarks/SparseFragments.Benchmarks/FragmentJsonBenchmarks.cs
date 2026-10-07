using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchFragmentThreeFields
{
    public int First { get; set; }
    public int Second { get; set; }
    public int Third { get; set; }
}

[MemoryDiagnoser]
public class FragmentJsonBenchmarks
{
    [Params(false, true)]
    public bool CustomNames { get; set; }

    [Params(false, true)]
    public bool Dense { get; set; }

    private JsonSerializerOptions _options = null!;
    private byte[] _json = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            PropertyNamingPolicy = CustomNames ? JsonNamingPolicy.CamelCase : null,
            PropertyNameCaseInsensitive = CustomNames,
        };
        _json = Encoding.UTF8.GetBytes(
            CustomNames
                ? Dense
                    ? "{\"FIRST\":1,\"SECOND\":2,\"THIRD\":3}"
                    : "{\"THIRD\":3}"
                : Dense
                    ? "{\"First\":1,\"Second\":2,\"Third\":3}"
                    : "{\"Third\":3}"
        );
        Validate(_json);
        Validate(
            Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(_json).Replace("T", "\\u0054").Replace("t", "\\u0074")
            )
        );
    }

    private void Validate(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        reader.Read();
        var value = BenchFragmentThreeFields.Fragment.JsonConverter.Read(
            ref reader,
            typeof(BenchFragmentThreeFields.Fragment),
            _options
        )!;
        if (
            value.Third.Value != 3
            || value.First.IsPresent != Dense
            || value.Second.IsPresent != Dense
            || (Dense && (value.First.Value != 1 || value.Second.Value != 2))
        )
            throw new InvalidOperationException(
                "Fragment JSON must preserve sparse values, naming policies, case handling, and escaped names."
            );
    }

    [Benchmark]
    public BenchFragmentThreeFields.Fragment Deserialize()
    {
        var reader = new Utf8JsonReader(_json);
        reader.Read();
        return BenchFragmentThreeFields.Fragment.JsonConverter.Read(
            ref reader,
            typeof(BenchFragmentThreeFields.Fragment),
            _options
        )!;
    }
}
