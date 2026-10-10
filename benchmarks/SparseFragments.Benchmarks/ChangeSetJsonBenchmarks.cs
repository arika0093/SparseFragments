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
        var alternateOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = _options.TypeInfoResolver,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        var alternateJson = JsonSerializer.SerializeToUtf8Bytes(
            _change.ToPayload(),
            alternateOptions
        );
        Validate(
            JsonSerializer
                .Deserialize<BenchChangeSetRebaseRecord.ChangePayload>(
                    alternateJson,
                    alternateOptions
                )!
                .ToChangeSet()
        );
        if (!Serialize().SequenceEqual(_json))
            throw new InvalidOperationException("Payload serialization must remain deterministic.");
        ReadEmptyFragment(BenchJsonCaseNames.Fragment.JsonConverter, _options);
        AssertNameCollision(BenchJsonCaseNames.Fragment.JsonConverter, alternateOptions);
        var namingPolicy = new BenchCountingJsonNamingPolicy();
        var collisionOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = _options.TypeInfoResolver,
            PropertyNamingPolicy = namingPolicy,
        };
        AssertNameCollision(BenchChangeSetRebaseRecord.Fragment.JsonConverter, collisionOptions);
        if (namingPolicy.Calls != 2)
            throw new InvalidOperationException(
                "Name validation must stop at the first collision."
            );
        using var document = JsonDocument.Parse(_json);
        using var reordered = new MemoryStream();
        using (var writer = new Utf8JsonWriter(reordered))
            WriteReordered(document.RootElement, writer);
        Validate(
            JsonSerializer
                .Deserialize<BenchChangeSetRebaseRecord.ChangePayload>(
                    reordered.ToArray(),
                    new JsonSerializerOptions(_options) { AllowOutOfOrderMetadataProperties = true }
                )!
                .ToChangeSet()
        );
    }

    private static void WriteReordered(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().Reverse())
            {
                writer.WritePropertyName(property.Name);
                WriteReordered(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteReordered(item, writer);
            writer.WriteEndArray();
        }
        else
            element.WriteTo(writer);
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

    private static void ReadEmptyFragment<T>(
        JsonConverter<T> converter,
        JsonSerializerOptions options
    )
    {
        var reader = new Utf8JsonReader(new byte[] { (byte)'{', (byte)'}' });
        reader.Read();
        converter.Read(ref reader, typeof(T), options);
    }

    private static void AssertNameCollision<T>(
        JsonConverter<T> converter,
        JsonSerializerOptions options
    )
    {
        try
        {
            ReadEmptyFragment(converter, options);
        }
        catch (JsonException error)
            when (error.Message.StartsWith(
                    "Multiple fragment members map to the same JSON property name.",
                    StringComparison.Ordinal
                )
            )
        {
            return;
        }
        throw new InvalidOperationException("Colliding JSON names must be rejected.");
    }

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
        var fragment = actual.GetValueOrDefault();
        if (fragment is not null)
        {
            var actualValues = fragment.Values.Value;
            var expectedValues = expected.Value!.Values.Value;
            if (
                fragment.Counter.Value != 1
                || fragment.Label.Value != "base"
                || actualValues is null
                || expectedValues is null
                || !actualValues.SequenceEqual(expectedValues)
            )
                throw new InvalidOperationException(
                    "ChangeSet JSON must preserve scalar and collection values."
                );
        }
    }

    [Benchmark]
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(_change.ToPayload(), _options);

    [Benchmark]
    public BenchChangeSetRebaseRecord.ChangeSet Deserialize() =>
        JsonSerializer
            .Deserialize<BenchChangeSetRebaseRecord.ChangePayload>(_json, _options)!
            .ToChangeSet();
}

[SparseFragmentModel]
public partial class BenchJsonCaseNames
{
    [JsonPropertyName("Name")]
    public int First { get; set; }

    [JsonPropertyName("name")]
    public int Second { get; set; }

    [JsonIgnore]
    public int Ignored { get; set; }
}

[SparseFragmentModel]
public partial class BenchJsonSingleName
{
    public int Value { get; set; }
}

public sealed class BenchCountingJsonNamingPolicy : JsonNamingPolicy
{
    public int Calls { get; private set; }

    public override string ConvertName(string name)
    {
        Calls++;
        return "same";
    }
}
