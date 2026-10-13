using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchMixedPayloadItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [SparseRedactBefore]
    public string Secret { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchMixedTemporaryItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    [SparseRedactBefore]
    public string Secret { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchMixedPayloadHolder
{
    public List<BenchMixedPayloadItem> Items { get; set; } = new();

    public Dictionary<string, BenchMixedPayloadItem> Map { get; set; } = new();

    [SparseRedactBefore]
    public List<BenchMixedPayloadItem> RedactedItems { get; set; } = new();

    [SparseRedactBefore]
    public Dictionary<string, string> Secrets { get; set; } = new();

    public List<BenchMixedTemporaryItem> TemporaryItems { get; set; } = new();
}

public enum MixedCollectionShape
{
    OrdinaryKeyed,
    OrdinaryDictionary,
    NestedKeyedBlind,
    NestedDictionaryBlind,
    ModelRemoval,
    ScalarEdit,
    ScalarRemoval,
    TemporaryBlind,
    AssignedTemporaryBlind,
}

[MemoryDiagnoser]
public class MixedCollectionPayloadBenchmarks
{
    [ParamsAllValues]
    public MixedCollectionShape Shape { get; set; }

    private BenchMixedPayloadHolder _prototype = null!;
    private BenchMixedPayloadHolder.ChangePayload _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _prototype = new BenchMixedPayloadHolder();
        for (var index = 0; index < 16; index++)
        {
            _prototype.Items.Add(CreateItem(index));
            _prototype.Map.Add("id-" + index, CreateItem(index));
            _prototype.RedactedItems.Add(CreateItem(index));
            _prototype.Secrets.Add("id-" + index, "secret-base-" + index);
            _prototype.TemporaryItems.Add(
                new BenchMixedTemporaryItem
                {
                    Id = Shape == MixedCollectionShape.AssignedTemporaryBlind ? index + 1 : 0,
                    TemporaryId = Guid.ParseExact(
                        (index + 1).ToString("X32", CultureInfo.InvariantCulture),
                        "N"
                    ),
                    Secret = "secret-base-" + index,
                }
            );
        }
        var after = _prototype.DeepClone();
        Edit(after);
        var beforeState = Optional<BenchMixedPayloadHolder.Fragment?>.Present(
            BenchMixedPayloadHolder.Fragment.From(_prototype)
        );
        var afterState = Optional<BenchMixedPayloadHolder.Fragment?>.Present(
            BenchMixedPayloadHolder.Fragment.From(after)
        );
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
            BenchMixedPayloadHolder.ChangeSet.Between(beforeState, afterState).ToPayload()
        );
        _payload = JsonSerializer.Deserialize<BenchMixedPayloadHolder.ChangePayload>(payloadJson)!;
        var originalJson = JsonSerializer.SerializeToUtf8Bytes(_prototype);
        var expectedJson = JsonSerializer.SerializeToUtf8Bytes(after);
        var blind =
            Shape
            is not MixedCollectionShape.OrdinaryKeyed
                and not MixedCollectionShape.OrdinaryDictionary;
        if (
            blind
            && Encoding
                .UTF8.GetString(payloadJson)
                .Contains("secret-base-", StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                "Redacted payloads must omit previous secret values."
            );
        for (var repeat = 0; repeat < 3; repeat++)
        {
            if (
                !_payload.TryApplyMixedTo(_prototype, out var updated, out var result)
                || result.HasConflicts
                || (result.WriteOnlyPaths.Count > 0) != blind
                || !expectedJson
                    .AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(updated))
                || !expectedJson
                    .AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(Apply()))
                || !originalJson
                    .AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(_prototype))
                || !payloadJson
                    .AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(_payload))
            )
                throw new InvalidOperationException(
                    "Mixed application must preserve both changes, write-only paths, payload bytes, and the original model."
                );
        }
    }

    [Benchmark]
    public BenchMixedPayloadHolder Apply()
    {
        if (!_payload.TryApplyMixedTo(_prototype, out var updated, out _) || updated is null)
            throw new InvalidOperationException("Mixed application must succeed.");
        return updated;
    }

    internal static void ValidateInputs()
    {
        foreach (var shape in Enum.GetValues<MixedCollectionShape>())
            new MixedCollectionPayloadBenchmarks { Shape = shape }.Setup();
        Console.WriteLine("Mixed collection payload inputs validated for all nine shapes.");
    }

    private static BenchMixedPayloadItem CreateItem(int index) =>
        new()
        {
            Id = "id-" + index,
            Name = "name-" + index,
            Secret = "secret-base-" + index,
        };

    private void Edit(BenchMixedPayloadHolder after)
    {
        for (var index = 0; index < 2; index++)
        {
            var key = "id-" + index;
            switch (Shape)
            {
                case MixedCollectionShape.OrdinaryKeyed:
                    after.Items[index].Name = "edited-" + index;
                    break;
                case MixedCollectionShape.OrdinaryDictionary:
                    after.Map[key].Name = "edited-" + index;
                    break;
                case MixedCollectionShape.NestedKeyedBlind:
                    after.Items[index].Secret = "secret-edited-" + index;
                    break;
                case MixedCollectionShape.NestedDictionaryBlind:
                    after.Map[key].Secret = "secret-edited-" + index;
                    break;
                case MixedCollectionShape.ModelRemoval:
                    after.RedactedItems.RemoveAt(0);
                    break;
                case MixedCollectionShape.ScalarEdit:
                    after.Secrets[key] = "secret-edited-" + index;
                    break;
                case MixedCollectionShape.ScalarRemoval:
                    after.Secrets.Remove(key);
                    break;
                case MixedCollectionShape.TemporaryBlind:
                case MixedCollectionShape.AssignedTemporaryBlind:
                    after.TemporaryItems[index].Secret = "secret-edited-" + index;
                    break;
                default:
                    throw new InvalidOperationException("Unknown mixed payload shape.");
            }
        }
    }
}
