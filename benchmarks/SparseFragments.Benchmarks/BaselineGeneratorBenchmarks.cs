using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

[MemoryDiagnoser]
public class BaselineGeneratorBenchmarks
{
    [Params(1, 4, 16)]
    public int ModelCount { get; set; }

    private CSharpCompilation _input = null!;
    private Compilation _generated = null!;
    private GeneratorDriver _incremental = null!;
    private int _revision;

    [GlobalSetup(Target = nameof(FreshGeneration))]
    public void SetupFresh()
    {
        _input = ThreeLayerGeneratorBenchmarks.CreateCollectionCompilation(ModelCount);
    }

    [GlobalSetup(Target = nameof(Compile))]
    public void SetupCompile()
    {
        SetupFresh();
        _generated = Generate(_input, out _);
        Compile();
    }

    [GlobalSetup(Target = nameof(IncrementalUnrelatedEdit))]
    public void SetupIncremental()
    {
        SetupFresh();
        _revision = 0;
        _ = Generate(_input, out _incremental);
        var expected = IncrementalUnrelatedEdit();
        for (var repeat = 0; repeat < 4; repeat++)
            if (IncrementalUnrelatedEdit() != expected)
                throw new InvalidOperationException(
                    "Comment edits must preserve generated output size."
                );
    }

    [Benchmark]
    public int FreshGeneration()
    {
        var generated = Generate(Fresh(_input), out var driver);
        GC.KeepAlive(generated);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark]
    public long Compile()
    {
        using var stream = new MemoryStream();
        var result = Fresh(_generated).Emit(stream);
        AssertNoErrors(result.Diagnostics);
        return stream.Length;
    }

    [Benchmark]
    public int IncrementalUnrelatedEdit()
    {
        _revision = 1 - _revision;
        var original = _input.SyntaxTrees.First();
        var edited = CSharpSyntaxTree.ParseText(
            original.ToString() + $"\n// edit {_revision}\n",
            path: original.FilePath
        );
        var compilation = _input.ReplaceSyntaxTree(original, edited);
        _incremental = _incremental.RunGenerators(compilation);
        return GeneratorStepTracking.TotalSourceBytes(_incremental.GetRunResult().Results.Single());
    }

    internal static void WriteSizes(string path)
    {
        var sizes = new List<object>();
        foreach (var count in new[] { 1, 4, 16 })
        {
            sizes.Add(
                MeasureSizes(
                    ThreeLayerGeneratorBenchmarks.CreateCollectionCompilation(count),
                    count
                )
            );
        }
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(sizes, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    internal static GeneratorOutputSize MeasureSizes(CSharpCompilation input, int modelCount)
    {
        var compilation = Generate(input, out var driver);
        var sources = driver
            .GetRunResult()
            .Results.Single()
            .GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal)
            .ToArray();
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = 0;
        foreach (var source in sources)
        {
            fingerprint.AppendData(Encoding.UTF8.GetBytes(source.HintName + "\0"));
            var text = Encoding.UTF8.GetBytes(source.SourceText.ToString());
            fingerprint.AppendData(text);
            bytes += text.Length;
        }
        using var stream = new MemoryStream();
        AssertNoErrors(compilation.Emit(stream).Diagnostics);
        return new GeneratorOutputSize
        {
            ModelCount = modelCount,
            GeneratedFiles = sources.Length,
            GeneratedSourceBytes = bytes,
            GeneratedSourceSha256 = Convert.ToHexString(fingerprint.GetHashAndReset()),
            AssemblyBytes = stream.Length,
        };
    }

    internal static CSharpCompilation Fresh(Compilation compilation) =>
        CSharpCompilation.Create(
            compilation.AssemblyName,
            compilation.SyntaxTrees,
            compilation.References,
            (CSharpCompilationOptions)compilation.Options
        );

    internal static Compilation Generate(CSharpCompilation input, out GeneratorDriver driver)
    {
        driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            input,
            out var output,
            out var diagnostics
        );
        AssertNoErrors(diagnostics);
        AssertNoErrors(driver.GetRunResult().Diagnostics);
        return output;
    }

    internal static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException(
                string.Join<Diagnostic>(Environment.NewLine, errors)
            );
    }
}

internal sealed record GeneratorOutputSize
{
    public int ModelCount { get; init; }
    public int GeneratedFiles { get; init; }
    public int GeneratedSourceBytes { get; init; }
    public string GeneratedSourceSha256 { get; init; } = string.Empty;
    public long AssemblyBytes { get; init; }
}
