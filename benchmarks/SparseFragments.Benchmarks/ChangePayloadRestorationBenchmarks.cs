using System.Text.Json;
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class ChangePayloadRestorationBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Dense { get; set; }

    private CertBenchKeyedHolder.ChangePayload _keyed = null!;
    private CertBenchDictHolder.ChangePayload _dictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        var fixture = new ChangePayloadConstructionBenchmarks { Size = Size, Dense = Dense };
        fixture.Setup();
        var keyedJson = JsonSerializer.SerializeToUtf8Bytes(fixture.Keyed());
        var dictionaryJson = JsonSerializer.SerializeToUtf8Bytes(fixture.Dictionary());
        _keyed = JsonSerializer.Deserialize<CertBenchKeyedHolder.ChangePayload>(keyedJson)!;
        _dictionary = JsonSerializer.Deserialize<CertBenchDictHolder.ChangePayload>(
            dictionaryJson
        )!;
        for (var repeat = 0; repeat < 3; repeat++)
        {
            if (
                !keyedJson
                    .AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(Keyed().ToPayload()))
                || !dictionaryJson
                    .AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(Dictionary().ToPayload()))
            )
                throw new InvalidOperationException(
                    "Repeated restoration must preserve the decoded payload and every transition."
                );
        }
    }

    [Benchmark]
    public CertBenchKeyedHolder.ChangeSet Keyed() => _keyed.ToChangeSet();

    [Benchmark]
    public CertBenchDictHolder.ChangeSet Dictionary() => _dictionary.ToChangeSet();
}
