using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum PatchJsonShape
{
    Empty,
    ScalarSet,
    ScalarUnset,
    Collection,
    RootSet,
    RootUnset,
    RootNull,
}

[MemoryDiagnoser]
public class PatchJsonBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        PatchJsonShape.Empty,
        PatchJsonShape.ScalarSet,
        PatchJsonShape.ScalarUnset,
        PatchJsonShape.Collection,
        PatchJsonShape.RootSet,
        PatchJsonShape.RootUnset,
        PatchJsonShape.RootNull
    )]
    public PatchJsonShape Shape { get; set; }

    private readonly JsonSerializerOptions _options = new()
    {
        TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
            BenchChangeSetJsonContext.Default,
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        ),
    };
    private Optional<BenchChangeSetRebaseRecord.Fragment?> _before;
    private Optional<BenchChangeSetRebaseRecord.Fragment?> _after;
    private BenchChangeSetRebaseRecord.Patch _patch = null!;
    private byte[] _json = null!;

    [GlobalSetup]
    public void Setup()
    {
        _before =
            Shape == PatchJsonShape.RootSet
                ? Optional<BenchChangeSetRebaseRecord.Fragment?>.Missing
                : State(false);
        _after = Shape switch
        {
            PatchJsonShape.Empty => _before,
            PatchJsonShape.RootUnset => Optional<BenchChangeSetRebaseRecord.Fragment?>.Missing,
            PatchJsonShape.RootNull => Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(null),
            _ => State(true),
        };
        _patch = BenchChangeSetRebaseRecord.Patch.Between(_before, _after);
        _json = Serialize();
        Validate(Deserialize());
        var escaped = System.Text.Encoding.UTF8.GetBytes(
            System
                .Text.Encoding.UTF8.GetString(_json)
                .Replace("\"kind\"", "\"ki\\u006ed\"")
                .Replace("\"value\"", "\"va\\u006cue\"")
                .Replace("\"set\"", "\"s\\u0065t\"")
                .Replace("\"unset\"", "\"un\\u0073et\"")
                .Replace("\"Counter\"", "\"Cou\\u006eter\"")
                .Replace("\"Label\"", "\"La\\u0062el\"")
                .Replace("\"Values\"", "\"Va\\u006cues\"")
                .Replace("\"$whole\"", "\"\\u0024whole\"")
        );
        Validate(JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.Patch>(escaped, _options)!);
        using (var document = JsonDocument.Parse(_json))
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject().Reverse())
                {
                    writer.WritePropertyName(property.Name);
                    writer.WriteStartObject();
                    foreach (var member in property.Value.EnumerateObject().Reverse())
                        member.WriteTo(writer);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            Validate(
                JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.Patch>(
                    stream.ToArray(),
                    _options
                )!
            );
        }
        foreach (
            var invalid in new[]
            {
                "{\"Counter\":{\"kind\":\"set\"}}",
                "{\"Counter\":{\"kind\":\"unset\",\"value\":1}}",
                "{\"Counter\":{\"kind\":\"bogus\"}}",
                "{\"Counter\":{\"kind\":null}}",
                "{\"Counter\":{\"value\":1}}",
                "{\"Counter\":{\"kind\":\"unset\",\"kind\":\"unset\"}}",
                "{\"Counter\":{\"kind\":\"set\",\"value\":1,\"value\":2}}",
                "{\"Counter\":{\"other\":\"unset\"}}",
                "{\"$whole\":{\"kind\":\"set\"}}",
                "{\"$whole\":{\"kind\":\"unset\",\"value\":null}}",
                "{\"$whole\":{\"kind\":\"bogus\"}}",
                "{\"$whole\":{\"kind\":null}}",
                "{\"$whole\":{\"value\":null}}",
                "{\"$whole\":{\"kind\":\"unset\",\"kind\":\"unset\"}}",
                "{\"$whole\":{\"kind\":\"set\",\"value\":null,\"value\":null}}",
                "{\"$whole\":{\"other\":\"unset\"}}",
                "{\"$whole\":{\"kind\":\"set\",\"value\":1}}",
                "{\"Counter\":{\"kind\":\"unset\"},\"Counter\":{\"kind\":\"unset\"}}",
                "{\"Counter\":{\"kind\":\"unset\"},\"Cou\\u006eter\":{\"kind\":\"unset\"}}",
                "{\"Unknown\":{\"kind\":\"unset\"}}",
            }
        )
        {
            try
            {
                JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.Patch>(invalid, _options);
            }
            catch (JsonException)
            {
                continue;
            }
            throw new InvalidOperationException(
                "Malformed scalar and whole patch operations must be rejected."
            );
        }
    }

    private Optional<BenchChangeSetRebaseRecord.Fragment?> State(bool changed)
    {
        if (changed && Shape == PatchJsonShape.ScalarUnset)
            return Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(new());
        var fragment = BenchChangeSetRebaseRecord.Fragment.From(
            new()
            {
                Counter = changed ? 1 : 0,
                Label = changed ? "new" : "base",
                Values = Enumerable
                    .Range(0, Size)
                    .Select(value =>
                        changed && Shape == PatchJsonShape.Collection ? value + 1 : value
                    )
                    .ToList(),
            }
        );
        return Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(fragment);
    }

    private void Validate(BenchChangeSetRebaseRecord.Patch patch)
    {
        if (!BenchChangeSetRebaseRecord.Patch.Between(patch.Apply(_before), _after).IsEmpty)
            throw new InvalidOperationException(
                "Patch JSON must preserve member values and presence."
            );
    }

    [Benchmark]
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(_patch, _options);

    [Benchmark]
    public BenchChangeSetRebaseRecord.Patch Deserialize() =>
        JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.Patch>(_json, _options)!;
}
