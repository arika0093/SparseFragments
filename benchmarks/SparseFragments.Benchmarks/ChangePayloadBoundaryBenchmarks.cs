using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum PayloadBoundaryShape
{
    Empty,
    LabelOnly,
    WholeCollections,
    DictionaryItems,
    KeyedItems,
}

[SparseFragmentModel]
public partial class BenchPayloadBoundary
{
    public string Label { get; set; } = "base";
    public Dictionary<string, int>? Entries { get; set; }
    public List<CertBenchKeyedItem>? Items { get; set; }
}

[MemoryDiagnoser]
public class ChangePayloadBoundaryBenchmarks
{
    [ParamsAllValues]
    public PayloadBoundaryShape Shape { get; set; }

    private BenchPayloadBoundary.ChangePayload _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = new BenchPayloadBoundary
        {
            Entries = Enumerable.Range(0, 16).ToDictionary(index => "key-" + index, index => index),
            Items = Enumerable
                .Range(0, 16)
                .Select(index => new CertBenchKeyedItem
                {
                    Id = "id-" + index,
                    Name = "name-" + index,
                })
                .ToList(),
        };
        var after = before.DeepClone();
        switch (Shape)
        {
            case PayloadBoundaryShape.LabelOnly:
                after.Label = "edited";
                break;
            case PayloadBoundaryShape.WholeCollections:
                before.Entries = null;
                before.Items = null;
                break;
            case PayloadBoundaryShape.DictionaryItems:
                after.Entries!["key-0"] = 100;
                break;
            case PayloadBoundaryShape.KeyedItems:
                after.Items![0].Name = "edited";
                break;
        }
        var original = Optional<BenchPayloadBoundary.Fragment?>.Present(
            BenchPayloadBoundary.Fragment.From(before)
        );
        var expected = Optional<BenchPayloadBoundary.Fragment?>.Present(
            BenchPayloadBoundary.Fragment.From(after)
        );
        var change = BenchPayloadBoundary.ChangeSet.Between(original, expected);
        var json = JsonSerializer.SerializeToUtf8Bytes(change.ToPayload());
        _payload = JsonSerializer.Deserialize<BenchPayloadBoundary.ChangePayload>(json)!;
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var restored = Restore();
            if (
                restored.IsEmpty != (Shape == PayloadBoundaryShape.Empty)
                || !BenchPayloadBoundary
                    .Patch.Between(restored.ToPatch().Apply(original), expected)
                    .IsEmpty
                || !json.AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(restored.ToPayload()))
            )
                throw new InvalidOperationException(
                    "Restoration must preserve the payload and the complete expected state."
                );
        }
    }

    [Benchmark]
    public BenchPayloadBoundary.ChangeSet Restore() => _payload.ToChangeSet();
}
