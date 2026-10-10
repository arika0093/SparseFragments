using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public enum PayloadGeneratorShape
{
    ScalarDictionary,
    ModelDictionary,
    ModelKeyed,
}

[MemoryDiagnoser]
public class PayloadGeneratorBenchmarks
{
    [Params(1, 4, 16)]
    public int ModelCount { get; set; }

    [ParamsAllValues]
    public PayloadGeneratorShape Shape { get; set; }

    private CSharpCompilation _input = null!;
    private Compilation _generated = null!;

    [GlobalSetup(Target = nameof(FreshGeneration))]
    public void SetupFresh()
    {
        _input = CreateCompilation(ModelCount, Shape);
    }

    [GlobalSetup(Target = nameof(Compile))]
    public void SetupCompile()
    {
        SetupFresh();
        _generated = BaselineGeneratorBenchmarks.Generate(_input, out _);
        var expected = Compile();
        if (Compile() != expected)
            throw new InvalidOperationException("Fresh compilation must preserve assembly size.");
    }

    [Benchmark]
    public int FreshGeneration()
    {
        var generated = BaselineGeneratorBenchmarks.Generate(
            BaselineGeneratorBenchmarks.Fresh(_input),
            out var driver
        );
        GC.KeepAlive(generated);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark]
    public long Compile()
    {
        using var stream = new MemoryStream();
        var result = BaselineGeneratorBenchmarks.Fresh(_generated).Emit(stream);
        BaselineGeneratorBenchmarks.AssertNoErrors(result.Diagnostics);
        return stream.Length;
    }

    internal static void WriteSizes(string path)
    {
        var sizes = new List<object>();
        foreach (var shape in Enum.GetValues<PayloadGeneratorShape>())
        {
            foreach (var count in new[] { 1, 4, 16 })
            {
                var size = BaselineGeneratorBenchmarks.MeasureSizes(
                    CreateCompilation(count, shape),
                    count
                );
                sizes.Add(
                    new
                    {
                        Shape = shape.ToString(),
                        size.ModelCount,
                        size.GeneratedFiles,
                        size.GeneratedSourceBytes,
                        size.GeneratedSourceSha256,
                        size.AssemblyBytes,
                    }
                );
            }
        }
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(sizes, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    private static CSharpCompilation CreateCompilation(int count, PayloadGeneratorShape shape)
    {
        var collection = shape switch
        {
            PayloadGeneratorShape.ScalarDictionary => "Dictionary<string, int>",
            PayloadGeneratorShape.ModelDictionary => "Dictionary<string, PayloadValue>",
            _ => "List<PayloadValue>",
        };
        var sources = new List<SyntaxTree>();
        if (shape != PayloadGeneratorShape.ScalarDictionary)
            sources.Add(
                CSharpSyntaxTree.ParseText(
                    """
                    using SparseFragments;
                    namespace PayloadBench;
                    [SparseFragmentModel]
                    public partial class PayloadValue
                    {
                        [SparseKey] public string Id { get; set; } = "";
                        public int Counter { get; set; }
                    }
                    """,
                    path: "PayloadValue.cs"
                )
            );
        for (var index = 0; index < count; index++)
            sources.Add(
                CSharpSyntaxTree.ParseText(
                    $$"""
                    using SparseFragments;
                    using System.Collections.Generic;
                    namespace PayloadBench;
                    [SparseFragmentModel]
                    public partial class PayloadModel{{index}}
                    {
                        public string Label { get; set; } = "";
                        public {{collection}} Items { get; set; } = new();
                    }
                    """,
                    path: $"PayloadModel{index}.cs"
                )
            );
        return ThreeLayerGeneratorBenchmarks
            .CreateCollectionCompilation(1)
            .WithAssemblyName("PayloadBenchGenerator")
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(sources);
    }
}
