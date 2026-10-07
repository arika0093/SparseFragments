using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum ChangeSetJsonShape
{
    Missing,
    Null,
    Scalar,
    Collection,
}

[JsonSerializable(typeof(BenchChangeSetRebaseRecord))]
public partial class BenchChangeSetJsonContext : JsonSerializerContext { }

[MemoryDiagnoser]
public class ChangeSetJsonBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        ChangeSetJsonShape.Missing,
        ChangeSetJsonShape.Null,
        ChangeSetJsonShape.Scalar,
        ChangeSetJsonShape.Collection
    )]
    public ChangeSetJsonShape Shape { get; set; }

    private readonly JsonSerializerOptions _options = new()
    {
        TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
            BenchChangeSetJsonContext.Default,
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        ),
    };
    private BenchChangeSetRebaseRecord.ChangeSet _change = null!;
    private Optional<BenchChangeSetRebaseRecord.Fragment?> _before;
    private byte[] _json = null!;

    [GlobalSetup]
    public void Setup()
    {
        _before = State(0);
        _change = BenchChangeSetRebaseRecord.ChangeSet.Between(_before, State(1));
        _json = Serialize();
        Validate(Deserialize());
        var escaped = System.Text.Encoding.UTF8.GetBytes(
            System
                .Text.Encoding.UTF8.GetString(_json)
                .Replace("\"before\"", "\"be\\u0066ore\"")
                .Replace("\"after\"", "\"a\\u0066ter\"")
        );
        Validate(
            JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.ChangeSet>(escaped, _options)!
        );
        using (var document = JsonDocument.Parse(_json))
        using (var reordered = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(reordered))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject().Reverse())
                    property.WriteTo(writer);
                writer.WriteEndObject();
            }
            Validate(
                JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.ChangeSet>(
                    reordered.ToArray(),
                    _options
                )!
            );
        }
        foreach (
            var invalid in new[]
            {
                "{\"before\":{\"state\":\"missing\"},\"before\":{\"state\":\"missing\"},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":\"missing\"},\"other\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":\"missing\"}}",
            }
        )
        {
            try
            {
                JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.ChangeSet>(invalid, _options);
            }
            catch (JsonException)
            {
                continue;
            }
            throw new InvalidOperationException("Invalid ChangeSet properties must be rejected.");
        }
    }

    private Optional<BenchChangeSetRebaseRecord.Fragment?> State(int step) =>
        Shape switch
        {
            ChangeSetJsonShape.Missing => Optional<BenchChangeSetRebaseRecord.Fragment?>.Missing,
            ChangeSetJsonShape.Null => Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(null),
            _ => Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(
                BenchChangeSetRebaseRecord.Fragment.From(
                    new()
                    {
                        Label = "base",
                        Counter = step,
                        Values = Enumerable
                            .Range(0, Size)
                            .Select(value =>
                                Shape == ChangeSetJsonShape.Collection ? value + step : value
                            )
                            .ToList(),
                    }
                )
            ),
        };

    private void Validate(BenchChangeSetRebaseRecord.ChangeSet change)
    {
        var actual = change.ToPatch().Apply(_before);
        var expected = State(1);
        if (
            actual.IsPresent != expected.IsPresent
            || (actual.GetValueOrDefault() is null) != (expected.GetValueOrDefault() is null)
            || change.IsEmpty != (Shape is ChangeSetJsonShape.Missing or ChangeSetJsonShape.Null)
        )
            throw new InvalidOperationException(
                "ChangeSet JSON must preserve root presence and emptiness."
            );
        if (
            actual.GetValueOrDefault() is { } fragment
            && (
                fragment.Counter.Value != 1
                || fragment.Label.Value != "base"
                || !fragment.Values.Value.SequenceEqual(expected.Value!.Values.Value)
            )
        )
            throw new InvalidOperationException(
                "ChangeSet JSON must preserve scalar and collection values."
            );
    }

    [Benchmark]
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(_change, _options);

    [Benchmark]
    public BenchChangeSetRebaseRecord.ChangeSet Deserialize() =>
        JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.ChangeSet>(_json, _options)!;
}
