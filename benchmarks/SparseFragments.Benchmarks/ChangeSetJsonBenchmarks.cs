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
        var alternateJson = JsonSerializer.SerializeToUtf8Bytes(_change, alternateOptions);
        Validate(
            JsonSerializer.Deserialize<BenchChangeSetRebaseRecord.ChangeSet>(
                alternateJson,
                alternateOptions
            )!
        );
        if (!Serialize().SequenceEqual(_json))
            throw new InvalidOperationException(
                "Fragment converter reuse must not retain serializer options."
            );
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
        namingPolicy = new BenchCountingJsonNamingPolicy();
        var singleOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = _options.TypeInfoResolver,
            PropertyNamingPolicy = namingPolicy,
        };
        ReadEmptyFragment(BenchJsonSingleName.Fragment.JsonConverter, singleOptions);
        if (namingPolicy.Calls != 1)
            throw new InvalidOperationException(
                "Single-member validation must evaluate its naming policy."
            );
        var escaped = System.Text.Encoding.UTF8.GetBytes(
            System
                .Text.Encoding.UTF8.GetString(_json)
                .Replace("\"before\"", "\"be\\u0066ore\"")
                .Replace("\"after\"", "\"a\\u0066ter\"")
                .Replace("\"state\"", "\"st\\u0061te\"")
                .Replace("\"value\"", "\"v\\u0061lue\"")
                .Replace("\"missing\"", "\"mi\\u0073sing\"")
                .Replace("\"null\"", "\"n\\u0075ll\"")
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
                "{\"before\":{\"state\":\"bogus\"},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":\"missing\",\"state\":\"null\"},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":null},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":\"value\"},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":\"value\",\"value\":null},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":\"missing\",\"value\":{}},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"state\":\"null\",\"value\":{}},\"after\":{\"state\":\"missing\"}}",
                "{\"before\":{\"other\":\"missing\"},\"after\":{\"state\":\"missing\"}}",
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
