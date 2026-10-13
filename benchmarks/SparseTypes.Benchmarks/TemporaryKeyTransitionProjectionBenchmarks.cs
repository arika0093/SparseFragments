using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseTypes;

public enum TemporaryKeyProjectionShape
{
    Assigned,
    Pending,
    Mixed,
}

[MemoryDiagnoser]
public class TemporaryKeyTransitionProjectionBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public TemporaryKeyProjectionShape Shape { get; set; }

    private BenchTemporaryPayloadHolder _before = null!;
    private BenchTemporaryPayloadHolder _after = null!;
    private Guid _temporaryKey;

    [GlobalSetup]
    public void Setup()
    {
        _before = new();
        _after = new();
        for (var index = 0; index < Size; index++)
        {
            var assigned =
                Shape == TemporaryKeyProjectionShape.Assigned
                || (Shape == TemporaryKeyProjectionShape.Mixed && index % 2 == 0);
            var temporaryKey = new Guid(index + 1, 0, 0, new byte[8]);
            var id = assigned ? index + 1 : 0;
            _before.Items.Add(
                new()
                {
                    Id = id,
                    TemporaryId = temporaryKey,
                    Name = "before",
                }
            );
            _after.Items.Add(
                new()
                {
                    Id = id,
                    TemporaryId = temporaryKey,
                    Name = "after",
                }
            );
            if (index == 0 || !assigned)
                _temporaryKey = temporaryKey;
        }
        var changes = BenchTemporaryPayloadHolder.ChangeSet.Between(_before, _after);
        var before = Optional<BenchTemporaryPayloadHolder.Fragment?>.Present(
            BenchTemporaryPayloadHolder.Fragment.From(_before)
        );
        var after = Optional<BenchTemporaryPayloadHolder.Fragment?>.Present(
            BenchTemporaryPayloadHolder.Fragment.From(_after)
        );
        var expected =
            Shape == TemporaryKeyProjectionShape.Assigned ? Size
            : Shape == TemporaryKeyProjectionShape.Pending ? 1
            : Size / 2 + 1;
        if (
            FirstView() != expected
            || FirstLookup() != (Shape != TemporaryKeyProjectionShape.Pending)
            || FirstTemporaryLookup() != (Shape != TemporaryKeyProjectionShape.Assigned)
            || JsonSerializer.Serialize(changes.ToPatch().Apply(before).Value!.ToModel())
                != JsonSerializer.Serialize(_after)
            || JsonSerializer.Serialize(changes.Invert().ToPatch().Apply(after).Value!.ToModel())
                != JsonSerializer.Serialize(_before)
        )
            throw new InvalidOperationException(
                "Typed projections must preserve assigned and temporary edits."
            );
    }

    [Benchmark]
    public int FirstView() =>
        BenchTemporaryPayloadHolder.ChangeSet.Between(_before, _after).Items.Edited.Count;

    [Benchmark]
    public bool FirstLookup() =>
        BenchTemporaryPayloadHolder.ChangeSet.Between(_before, _after).Items.GetChange(1).IsChanged;

    [Benchmark]
    public bool FirstTemporaryLookup() =>
        BenchTemporaryPayloadHolder
            .ChangeSet.Between(_before, _after)
            .Items.GetTemporaryChange(_temporaryKey)
            .IsChanged;
}
