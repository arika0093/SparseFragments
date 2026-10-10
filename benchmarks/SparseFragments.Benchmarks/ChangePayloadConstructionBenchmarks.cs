using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[MemoryDiagnoser]
public class ChangePayloadConstructionBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Dense { get; set; }

    private CertBenchKeyedHolder.ChangeSet _keyed = null!;
    private CertBenchDictHolder.ChangeSet _dictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = new CertBenchKeyedHolder
        {
            Label = "keyed",
            Items = Enumerable
                .Range(0, Size)
                .Select(index => new CertBenchKeyedItem
                {
                    Id = "id-" + index,
                    Name = "name-" + index,
                    Count = index,
                })
                .ToList(),
        };
        var after = before.DeepClone();
        var edits = Dense ? Size : 1;
        for (var index = 0; index < edits; index++)
            after.Items[index].Name = "edited-" + index;
        after.Items.Add(new CertBenchKeyedItem { Id = "new", Name = "added" });
        var keyedBefore = Optional<CertBenchKeyedHolder.Fragment?>.Present(
            CertBenchKeyedHolder.Fragment.From(before)
        );
        var keyedAfter = Optional<CertBenchKeyedHolder.Fragment?>.Present(
            CertBenchKeyedHolder.Fragment.From(after)
        );
        _keyed = CertBenchKeyedHolder.ChangeSet.Between(keyedBefore, keyedAfter);
        var dictionaryBefore = new CertBenchDictHolder
        {
            Label = "dictionary",
            Entries = Enumerable
                .Range(0, Size)
                .ToDictionary(index => "key-" + index, index => "value-" + index),
        };
        var dictionaryAfter = dictionaryBefore.DeepClone();
        for (var index = 0; index < edits; index++)
            dictionaryAfter.Entries["key-" + index] = "edited-" + index;
        dictionaryAfter.Entries.Add("new", "added");
        var dictBefore = Optional<CertBenchDictHolder.Fragment?>.Present(
            CertBenchDictHolder.Fragment.From(dictionaryBefore)
        );
        var dictAfter = Optional<CertBenchDictHolder.Fragment?>.Present(
            CertBenchDictHolder.Fragment.From(dictionaryAfter)
        );
        _dictionary = CertBenchDictHolder.ChangeSet.Between(dictBefore, dictAfter);
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var keyed = JsonSerializer
                .Deserialize<CertBenchKeyedHolder.ChangePayload>(
                    JsonSerializer.SerializeToUtf8Bytes(Keyed())
                )!
                .ToChangeSet();
            var dictionary = JsonSerializer
                .Deserialize<CertBenchDictHolder.ChangePayload>(
                    JsonSerializer.SerializeToUtf8Bytes(Dictionary())
                )!
                .ToChangeSet();
            if (
                !CertBenchKeyedHolder
                    .Patch.Between(keyed.ToPatch().Apply(keyedBefore), keyedAfter)
                    .IsEmpty
                || !CertBenchDictHolder
                    .Patch.Between(dictionary.ToPatch().Apply(dictBefore), dictAfter)
                    .IsEmpty
            )
                throw new InvalidOperationException(
                    "Payload construction must preserve every edit and addition."
                );
        }
    }

    [Benchmark]
    public CertBenchKeyedHolder.ChangePayload Keyed() => _keyed.ToPayload();

    [Benchmark]
    public CertBenchDictHolder.ChangePayload Dictionary() => _dictionary.ToPayload();
}
