using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments;
using SparseFragments.Generator;

/// <summary>
/// Incremental-generator scale benchmarks for the promoted-model pipeline
/// (issue #18). Distinguishes cold generation from one-file incremental
/// edits over 10 / 100 / 1000 roots sharing a single promoted partial type.
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
    private int _unrelatedEdits;
    private int _sharedEdits;
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
        _unrelatedCompilation = _baseCompilation;
        _unrelatedDriver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _sharedCompilation = _baseCompilation;
        _sharedDriver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _unrelatedEdits = 0;
        _sharedEdits = 0;
        _preparedFor = RootCount;
    }

    [Benchmark(Description = "Generator cold: full generation over N roots sharing one promoted type")]
    public int ColdGeneration()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return driver.GetRunResult().Results.SelectMany(static result => result.GeneratedSources).Count();
    }

    [Benchmark(Description = "Generator incremental: edit a single unrelated root")]
    public int IncrementalUnrelatedEdit()
    {
        EnsurePrepared();
        _unrelatedEdits++;
        var updated = ScaleCompilations.WithUnrelatedEdit(_unrelatedCompilation, _unrelatedEdits);
        _unrelatedDriver = _unrelatedDriver.RunGenerators(updated);
        _unrelatedCompilation = updated;
        return _unrelatedDriver.GetRunResult().Results.SelectMany(static result => result.GeneratedSources).Count();
    }

    [Benchmark(Description = "Generator incremental: edit the shared promoted type")]
    public int IncrementalSharedEdit()
    {
        EnsurePrepared();
        _sharedEdits++;
        var updated = ScaleCompilations.WithSharedEdit(_sharedCompilation, _sharedEdits);
        _sharedDriver = _sharedDriver.RunGenerators(updated);
        _sharedCompilation = updated;
        return _sharedDriver.GetRunResult().Results.SelectMany(static result => result.GeneratedSources).Count();
    }

    private static class ScaleCompilations
    {
        private const string SharedPath = "ScaleShared.cs";

        internal static CSharpCompilation Create(int rootCount)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SharedPath] =
                    """
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
                files[$"ScaleRoot{index}.cs"] =
                    $$"""
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
            return WithAppendedMember(compilation, $"ScaleRoot{index}.cs", $"public int UnrelatedEdit{edit} {{ get; set; }}");
        }

        internal static CSharpCompilation WithSharedEdit(CSharpCompilation compilation, int edit)
        {
            return WithAppendedMember(compilation, SharedPath, $"public string SharedEdit{edit} {{ get; set; }} = \"\";");
        }

        private static CSharpCompilation WithAppendedMember(
            CSharpCompilation compilation,
            string path,
            string member
        )
        {
            var oldTree = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
            var oldText = oldTree.GetText().ToString();
            var insertAt = oldText.LastIndexOf('}');
            var newText = oldText.Insert(insertAt, "    " + member + "\n");
            var newTree = CSharpSyntaxTree.ParseText(newText, path: path);
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
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(
                    NullableContextOptions.Enable
                )
            );
        }
    }
}
