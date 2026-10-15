using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes;

/// <summary>Measures incremental reuse for models with many keyed collection members.</summary>
[MemoryDiagnoser]
public class KeyedCollectionIncrementalBenchmarks
{
    [Params(10, 50, 100)]
    public int CollectionCount { get; set; }

    private CSharpCompilation _compilation = null!;
    private GeneratorDriver _driver = null!;
    private int _edit;

    [GlobalSetup]
    public void Setup()
    {
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(CreateRootSource(), path: "Root.cs"),
        };
        for (var index = 0; index < CollectionCount; index++)
        {
            trees.Add(
                CSharpSyntaxTree.ParseText(CreateElementSource(index), path: $"Element{index}.cs")
            );
        }

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trustedAssemblies
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        _compilation = CSharpCompilation.Create(
            "KeyedCollectionIncrementalBenchmark",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        _driver = GeneratorStepTracking.CreateTrackedDriver().RunGenerators(_compilation);
        var diagnostics = _driver.GetRunResult().Diagnostics;
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics));
        }
    }

    [Benchmark(Description = "Incremental rerun: unchanged keyed collections")]
    public int IncrementalNoOpEdit_RenderExecutions()
    {
        _edit = 1 - _edit;
        var oldTree = _compilation.SyntaxTrees.Single(tree => tree.FilePath == "Root.cs");
        var source = oldTree.GetText().ToString();
        var updatedSource = source.Replace(
            _edit == 0 ? "public  List<KeyedElement0>" : "public List<KeyedElement0>",
            _edit == 0 ? "public List<KeyedElement0>" : "public  List<KeyedElement0>",
            StringComparison.Ordinal
        );
        var updatedTree = CSharpSyntaxTree.ParseText(updatedSource, path: "Root.cs");
        _compilation = (CSharpCompilation)_compilation.ReplaceSyntaxTree(oldTree, updatedTree);
        _driver = _driver.RunGenerators(_compilation);
        var renderExecutions = CountRenderedOutputs(_driver.GetRunResult().Results.Single());
        if (renderExecutions != 0)
        {
            throw new InvalidOperationException(
                $"A semantic no-op unexpectedly reran {renderExecutions} render outputs."
            );
        }

        return renderExecutions;
    }

    private static int CountRenderedOutputs(GeneratorRunResult result)
    {
        if (!result.TrackedSteps.TryGetValue("SparseTypesGenerator.Output", out var steps))
        {
            return 0;
        }

        var executions = 0;
        foreach (var step in steps)
        {
            foreach (var output in step.Outputs)
            {
                if (output.Reason != IncrementalStepRunReason.Cached)
                {
                    executions++;
                }
            }
        }

        return executions;
    }

    private string CreateRootSource()
    {
        var source = new StringBuilder(
            "using System.Collections.Generic;\nusing SparseTypes;\n"
                + "[SparseModel]\npublic partial class KeyedRoot\n{\n"
        );
        for (var index = 0; index < CollectionCount; index++)
        {
            source
                .Append("public List<KeyedElement")
                .Append(index)
                .Append("> Items")
                .Append(index)
                .Append(" { get; set; } = new();\n");
        }

        return source.Append("}\n").ToString();
    }

    private static string CreateElementSource(int index) =>
        $$"""
            using SparseTypes;
            public partial class KeyedElement{{index}}
            {
                [SparseKey]
                public int Id { get; set; }
                public string Value { get; set; } = "";
            }
            """;
}
