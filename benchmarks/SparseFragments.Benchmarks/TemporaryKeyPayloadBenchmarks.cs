using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchTemporaryPayloadItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class BenchTemporaryPayloadHolder
{
    public List<BenchTemporaryPayloadItem> Items { get; set; } = new();
}

[MemoryDiagnoser]
public class TemporaryKeyPayloadBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Dense { get; set; }

    [Params(false, true)]
    public bool Assigned { get; set; }

    private BenchTemporaryPayloadHolder.ChangePayload _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = new BenchTemporaryPayloadHolder
        {
            Items = Enumerable.Range(1, Size).Select(CreateItem).ToList(),
        };
        var after = before.DeepClone();
        for (var index = 0; index < (Dense ? Size : 1); index++)
            after.Items[index].Name = "edited-" + index;
        after.Items.Add(CreateItem(Size + 1));
        var original = Optional<BenchTemporaryPayloadHolder.Fragment?>.Present(
            BenchTemporaryPayloadHolder.Fragment.From(before)
        );
        var expected = Optional<BenchTemporaryPayloadHolder.Fragment?>.Present(
            BenchTemporaryPayloadHolder.Fragment.From(after)
        );
        var change = BenchTemporaryPayloadHolder.ChangeSet.Between(original, expected);
        var json = JsonSerializer.SerializeToUtf8Bytes(change.ToPayload());
        var originalModelJson = JsonSerializer.SerializeToUtf8Bytes(before);
        var expectedModelJson = JsonSerializer.SerializeToUtf8Bytes(after);
        _payload = JsonSerializer.Deserialize<BenchTemporaryPayloadHolder.ChangePayload>(json)!;
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var restored = Restore();
            var applied = restored.ToPatch().Apply(original);
            if (
                !expectedModelJson
                    .AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(applied.Value!.ToModel()))
            )
                throw new InvalidOperationException(
                    "Restored edits must apply to the expected state."
                );
            if (
                !originalModelJson
                    .AsSpan()
                    .SequenceEqual(
                        JsonSerializer.SerializeToUtf8Bytes(
                            restored.Invert().ToPatch().Apply(expected).Value!.ToModel()
                        )
                    )
            )
                throw new InvalidOperationException(
                    "Inverse edits must restore the original state."
                );
            if (
                !json.AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(restored.ToPayload()))
            )
                throw new InvalidOperationException(
                    "Restoration must preserve exact payload JSON."
                );
            if (
                restored.Items.GetTemporaryChange(before.Items[0].TemporaryId!.Value).IsEmpty
                != Assigned
            )
                throw new InvalidOperationException(
                    "Temporary lookup must respect assigned-key precedence."
                );
            foreach (var item in restored.Items)
                if (item.TemporaryKey.HasValue == Assigned)
                    throw new InvalidOperationException(
                        "Only unassigned items may expose temporary identity."
                    );
        }
    }

    [Benchmark]
    public BenchTemporaryPayloadHolder.ChangeSet Restore() => _payload.ToChangeSet();

    internal static void ValidateInputs()
    {
        foreach (var size in new[] { 16, 2048 })
        foreach (var dense in new[] { false, true })
        foreach (var assigned in new[] { false, true })
            new TemporaryKeyPayloadBenchmarks
            {
                Size = size,
                Dense = dense,
                Assigned = assigned,
            }.Setup();
        Console.WriteLine("Temporary-key payload inputs validated for all eight cases.");
    }

    private BenchTemporaryPayloadItem CreateItem(int index) =>
        new()
        {
            Id = Assigned ? index : 0,
            TemporaryId = Guid.ParseExact(index.ToString("X32", CultureInfo.InvariantCulture), "N"),
            Name = "name-" + index,
        };
}
