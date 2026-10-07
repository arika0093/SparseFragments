using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

[MemoryDiagnoser]
public class JsonPatchEmptyDiffBenchmarks
{
    private static readonly byte[] EmptyPatch = { 91, 93 };

    [Params(false, true)]
    public bool Absent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var first = Diff();
        var second = Diff();
        if (!first.SequenceEqual(new byte[] { 91, 93 }) || ReferenceEquals(first, second))
        {
            throw new InvalidOperationException("Empty diffs must return independent JSON arrays.");
        }
        first[0] = 0;
        if (!Diff().SequenceEqual(new byte[] { 91, 93 }))
        {
            throw new InvalidOperationException(
                "Mutating returned bytes must not affect subsequent diffs."
            );
        }
        _ = SparseJsonPatchBridge.Apply(
            null,
            Absent,
            second,
            StringComparison.Ordinal,
            out var actualAbsent
        );
        if (actualAbsent != Absent)
        {
            throw new InvalidOperationException("An empty diff must preserve root presence.");
        }
        if (Apply() is not null)
        {
            throw new InvalidOperationException(
                "Empty root patches must preserve JSON null or absence."
            );
        }
        foreach (var beforeAbsent in new[] { false, true })
        {
            var transition = SparseJsonPatchBridge.Diff(null, beforeAbsent, null, !beforeAbsent);
            using var document = JsonDocument.Parse(transition);
            var operations = document.RootElement;
            if (
                operations.GetArrayLength() != 1
                || operations[0].GetProperty("op").GetString() != (beforeAbsent ? "add" : "remove")
                || operations[0].GetProperty("path").GetString() != ""
            )
            {
                throw new InvalidOperationException(
                    "JSON null and an absent root must remain distinct."
                );
            }
            _ = SparseJsonPatchBridge.Apply(
                null,
                beforeAbsent,
                transition,
                StringComparison.Ordinal,
                out var afterAbsent
            );
            if (afterAbsent == beforeAbsent)
            {
                throw new InvalidOperationException("Root transition diffs must change presence.");
            }
        }
    }

    [Benchmark]
    public byte[] Diff() => SparseJsonPatchBridge.Diff(null, Absent, null, Absent);

    [Benchmark]
    public JsonNode? Apply() =>
        SparseJsonPatchBridge.Apply(null, Absent, EmptyPatch, StringComparison.Ordinal, out _);
}
