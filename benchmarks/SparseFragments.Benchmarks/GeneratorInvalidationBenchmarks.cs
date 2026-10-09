using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SparseFragments;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

/// <summary>
/// Incremental-generator scale benchmarks for the promoted-model pipeline
/// (issue #18, extended by #61). Distinguishes cold generation from one-file
/// incremental edits over 10 / 100 / 1000 roots sharing a single promoted
/// partial type, and additionally records incremental step caching:
/// <c>CachedSteps</c> counts tracked outputs that stay cached after an
/// unrelated-root edit (higher is better), while <c>RecomputedSteps</c> counts
/// tracked outputs that recompute after a shared promoted-type edit.
/// Model-shape dimensions beyond root count (properties per root, nesting
/// depth/fan-out, keyed collections, dictionary members, promoted-model count)
/// live in <c>GeneratorModelShapeBenchmarks</c>; per-stage cached-vs-recomputed
/// expectations are pinned by deterministic tests in
/// <c>GeneratorStepTrackingTests</c>.
/// </summary>
[MemoryDiagnoser]
public class GeneratorInvalidationBenchmarks
{
    [Params(10, 100, 1000)]
    public int RootCount { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _unrelatedDriver = null!;
    private CSharpCompilation _unrelatedCompilation = null!;
    private GeneratorDriver _sharedDriver = null!;
    private CSharpCompilation _sharedCompilation = null!;
    private GeneratorDriver _unrelatedTrackedDriver = null!;
    private CSharpCompilation _unrelatedTrackedCompilation = null!;
    private GeneratorDriver _sharedTrackedDriver = null!;
    private CSharpCompilation _sharedTrackedCompilation = null!;
    private int _unrelatedEdits;
    private int _sharedEdits;
    private int _unrelatedTrackedEdits;
    private int _sharedTrackedEdits;
    private int _coldCompilations;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == RootCount && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = ScaleCompilations.Create(RootCount);
        var errors = _baseCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        if (errors.Any())
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
        _unrelatedCompilation = _baseCompilation;
        _unrelatedDriver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _sharedCompilation = _baseCompilation;
        _sharedDriver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _unrelatedTrackedCompilation = _baseCompilation;
        _unrelatedTrackedDriver = GeneratorStepTracking
            .CreateTrackedDriver()
            .RunGenerators(_baseCompilation);
        _sharedTrackedCompilation = _baseCompilation;
        _sharedTrackedDriver = GeneratorStepTracking
            .CreateTrackedDriver()
            .RunGenerators(_baseCompilation);
        ValidateDriver(_unrelatedDriver);
        ValidateDriver(_sharedDriver);
        ValidateDriver(_unrelatedTrackedDriver);
        ValidateDriver(_sharedTrackedDriver);
        var freshCompilation = CreateFreshCompilation();
        if (ReferenceEquals(freshCompilation.Assembly, _baseCompilation.Assembly))
        {
            throw new InvalidOperationException(
                "Cold compilation must have a distinct assembly symbol."
            );
        }
        var freshDriver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(freshCompilation);
        ValidateDriver(freshDriver);
        ValidateEdits(ScaleCompilations.WithUnrelatedEdit);
        ValidateEdits(ScaleCompilations.WithSharedEdit);
        Console.WriteLine(
            "Prepared source fingerprint ("
                + RootCount
                + " roots): "
                + GetSourceFingerprint(_unrelatedDriver)
        );
        Console.WriteLine(
            "Fresh source fingerprint ("
                + RootCount
                + " roots): "
                + GetSourceFingerprint(freshDriver)
        );
        _unrelatedEdits = 0;
        _sharedEdits = 0;
        _unrelatedTrackedEdits = 0;
        _sharedTrackedEdits = 0;
        _preparedFor = RootCount;
    }

    private void ValidateEdits(Func<CSharpCompilation, int, CSharpCompilation> edit)
    {
        var compilation = _baseCompilation;
        var propertyCount = CountProperties(compilation);
        var driver = _unrelatedDriver;
        var fingerprint = GetSourceFingerprint(driver);
        for (var revision = 1; revision <= RootCount + 1; revision++)
        {
            compilation = edit(compilation, revision);
            if (CountProperties(compilation) != propertyCount)
            {
                throw new InvalidOperationException(
                    "Incremental edits must preserve property count."
                );
            }
        }
        foreach (var revision in new[] { RootCount + 2, RootCount + 3 })
        {
            compilation = edit(compilation, revision);
            driver = driver.RunGenerators(compilation);
            ValidateDriver(driver);
            var updatedFingerprint = GetSourceFingerprint(driver);
            if (updatedFingerprint == fingerprint)
            {
                throw new InvalidOperationException(
                    "Incremental edit must change generated output."
                );
            }
            fingerprint = updatedFingerprint;
        }
        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        if (errors.Any())
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    private static int CountProperties(CSharpCompilation compilation) =>
        compilation.SyntaxTrees.Sum(tree =>
            tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().Count()
        );

    private void ValidateDriver(GeneratorDriver driver)
    {
        var run = driver.GetRunResult();
        var result = run.Results.Single();
        if (result.Exception is not null)
        {
            throw new InvalidOperationException("Benchmark generation failed.", result.Exception);
        }
        var errors = run.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        if (errors.Any())
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
        var hints = result
            .GeneratedSources.Select(source => source.HintName)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < RootCount; index++)
        {
            var typeName = "global::ScaleRoot" + index;
            var expected =
                SparseNaming.Sanitize(typeName, CancellationToken.None)
                + "_"
                + SparseNaming.GetStableTypeHash(typeName, CancellationToken.None)
                + ".SparseFragments.g.cs";
            if (!hints.Contains(expected))
            {
                throw new InvalidOperationException("Missing generated root: " + expected);
            }
        }
    }

    private static string GetSourceFingerprint(GeneratorDriver driver)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (
            var source in driver
                .GetRunResult()
                .Results.Single()
                .GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal)
        )
        {
            var name = Encoding.UTF8.GetBytes(source.HintName);
            var text = Encoding.UTF8.GetBytes(source.SourceText.ToString());
            hash.AppendData(BitConverter.GetBytes(name.Length));
            hash.AppendData(name);
            hash.AppendData(BitConverter.GetBytes(text.Length));
            hash.AppendData(text);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    [Benchmark(
        Description = "Generator cold: full generation over N roots sharing one promoted type"
    )]
    public int ColdGeneration()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Count();
    }

    [Benchmark(Description = "Generator cold: fresh compilation and driver over N roots")]
    public int ColdCompilationGeneration()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(CreateFreshCompilation());
        return driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Count();
    }

    private CSharpCompilation CreateFreshCompilation() =>
        _baseCompilation.WithAssemblyName("SparseGeneratorScaleProbe_" + ++_coldCompilations);

    [Benchmark(Description = "Generator incremental: edit a single unrelated root")]
    public int IncrementalUnrelatedEdit()
    {
        EnsurePrepared();
        _unrelatedEdits++;
        var updated = ScaleCompilations.WithUnrelatedEdit(_unrelatedCompilation, _unrelatedEdits);
        _unrelatedDriver = _unrelatedDriver.RunGenerators(updated);
        _unrelatedCompilation = updated;
        return _unrelatedDriver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Count();
    }

    [Benchmark(Description = "Generator incremental: edit the shared promoted type")]
    public int IncrementalSharedEdit()
    {
        EnsurePrepared();
        _sharedEdits++;
        var updated = ScaleCompilations.WithSharedEdit(_sharedCompilation, _sharedEdits);
        _sharedDriver = _sharedDriver.RunGenerators(updated);
        _sharedCompilation = updated;
        return _sharedDriver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Count();
    }

    [Benchmark(
        Description = "Generator steps: cached tracked outputs after an unrelated-root edit"
    )]
    public int IncrementalUnrelatedEdit_CachedSteps()
    {
        EnsurePrepared();
        _unrelatedTrackedEdits++;
        var updated = ScaleCompilations.WithUnrelatedEdit(
            _unrelatedTrackedCompilation,
            _unrelatedTrackedEdits
        );
        _unrelatedTrackedDriver = _unrelatedTrackedDriver.RunGenerators(updated);
        _unrelatedTrackedCompilation = updated;
        return GeneratorStepTracking.CachedOutputs(
            _unrelatedTrackedDriver.GetRunResult().Results.Single()
        );
    }

    [Benchmark(
        Description = "Generator steps: recomputed tracked outputs after a shared-type edit"
    )]
    public int IncrementalSharedEdit_RecomputedSteps()
    {
        EnsurePrepared();
        _sharedTrackedEdits++;
        var updated = ScaleCompilations.WithSharedEdit(
            _sharedTrackedCompilation,
            _sharedTrackedEdits
        );
        _sharedTrackedDriver = _sharedTrackedDriver.RunGenerators(updated);
        _sharedTrackedCompilation = updated;
        return GeneratorStepTracking.RecomputedOutputs(
            _sharedTrackedDriver.GetRunResult().Results.Single()
        );
    }

    private static class ScaleCompilations
    {
        private const string SharedPath = "ScaleShared.cs";

        internal static CSharpCompilation Create(int rootCount)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SharedPath] = """
                    using SparseFragments;
                    public partial class ScaleShared
                    {
                        public string A { get; set; } = "";
                        public string B { get; set; } = "";
                    }
                    """,
            };
            for (var index = 0; index < rootCount; index++)
            {
                files[$"ScaleRoot{index}.cs"] = $$"""
                    using SparseFragments;
                    [SparseFragmentModel]
                    public partial class ScaleRoot{{index}}
                    {
                        public string Label { get; set; } = "";
                        public ScaleShared Child { get; set; } = new();
                    }
                    """;
            }

            return CreateCompilation(files);
        }

        internal static CSharpCompilation WithUnrelatedEdit(CSharpCompilation compilation, int edit)
        {
            var rootCount = compilation.SyntaxTrees.Count(tree => tree.FilePath != SharedPath);
            var index = edit % rootCount;
            return WithRenamedProperty(compilation, $"ScaleRoot{index}.cs", edit);
        }

        internal static CSharpCompilation WithSharedEdit(CSharpCompilation compilation, int edit)
        {
            return WithRenamedProperty(compilation, SharedPath, edit);
        }

        private static CSharpCompilation WithRenamedProperty(
            CSharpCompilation compilation,
            string path,
            int edit
        )
        {
            var oldTree = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
            var root = oldTree.GetRoot();
            var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().First();
            var renamed = property.WithIdentifier(
                SyntaxFactory.Identifier("Edited" + edit).WithTriviaFrom(property.Identifier)
            );
            var newTree = oldTree.WithRootAndOptions(
                root.ReplaceNode(property, renamed),
                oldTree.Options
            );
            return (CSharpCompilation)compilation.ReplaceSyntaxTree(oldTree, newTree);
        }

        private static CSharpCompilation CreateCompilation(Dictionary<string, string> files)
        {
            var trees = files
                .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
                .ToArray();
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
                Path.PathSeparator
            );
            var references = trusted
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToList();
            references.Add(
                MetadataReference.CreateFromFile(
                    typeof(SparseFragmentModelAttribute).Assembly.Location
                )
            );
            return CSharpCompilation.Create(
                "SparseGeneratorScaleProbe",
                trees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary
                ).WithNullableContextOptions(NullableContextOptions.Enable)
            );
        }
    }
}
