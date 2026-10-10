using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments;
using SparseFragments.Generator;

/// <summary>
/// Model-shape scaling benchmarks (issue #61): beyond the root-count axis of
/// <see cref="GeneratorInvalidationBenchmarks"/>, generation cost is measured
/// over properties per root, nested structural depth, nested fan-out, keyed
/// structural collections, dictionary members, and promoted-model count.
/// Each benchmark returns total generated source bytes (source count is
/// asserted by <c>GeneratorStepTrackingTests</c>); allocation is recorded by
/// <c>MemoryDiagnoser</c>. Only cold generation and one-file incremental edits
/// are measured; step invalidation is attributed by the tracked benchmarks and
/// never optimized from wall-clock noise alone.
/// </summary>
[MemoryDiagnoser]
public class GeneratorWideModelBenchmarks
{
    [Params(8, 32, 128)]
    public int PropertyCount { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _driver = null!;
    private int _edits;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == PropertyCount && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = ShapeCompilations.CreateWide(10, PropertyCount);
        _driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _edits = 0;
        _preparedFor = PropertyCount;
    }

    [Benchmark(Description = "Generator cold: wide roots by property count")]
    public int ColdGenerationBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator incremental: unrelated edit on a wide root")]
    public int IncrementalUnrelatedEditBytes()
    {
        EnsurePrepared();
        _edits = 1 - _edits;
        var updated = ShapeCompilations.WithAppendedMember(
            _baseCompilation,
            "WideRoot0.cs",
            $"public int UnrelatedEdit{_edits} {{ get; set; }}"
        );
        _driver = _driver.RunGenerators(updated);
        return GeneratorStepTracking.TotalSourceBytes(_driver.GetRunResult().Results.Single());
    }
}

/// <summary>Nested structural depth benchmarks: a root over a chain of D promoted partials.</summary>
[MemoryDiagnoser]
public class GeneratorNestedDepthBenchmarks
{
    [Params(1, 3, 6)]
    public int Depth { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _driver = null!;
    private int _edits;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == Depth && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = ShapeCompilations.CreateDeep(Depth);
        _driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _edits = 0;
        _preparedFor = Depth;
    }

    [Benchmark(Description = "Generator cold: nested structural depth")]
    public int ColdGenerationBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator incremental: unrelated edit on a nested root")]
    public int IncrementalUnrelatedEditBytes()
    {
        EnsurePrepared();
        _edits = 1 - _edits;
        var updated = ShapeCompilations.WithAppendedMember(
            _baseCompilation,
            "DeepRoot.cs",
            $"public int UnrelatedEdit{_edits} {{ get; set; }}"
        );
        _driver = _driver.RunGenerators(updated);
        return GeneratorStepTracking.TotalSourceBytes(_driver.GetRunResult().Results.Single());
    }
}

/// <summary>Nested fan-out benchmarks: a root referencing F distinct promoted partials.</summary>
[MemoryDiagnoser]
public class GeneratorFanOutBenchmarks
{
    [Params(2, 8, 32)]
    public int FanOut { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _driver = null!;
    private int _edits;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == FanOut && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = ShapeCompilations.CreateFanOut(FanOut);
        _driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _edits = 0;
        _preparedFor = FanOut;
    }

    [Benchmark(Description = "Generator cold: nested fan-out")]
    public int ColdGenerationBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator incremental: unrelated edit on a fan-out root")]
    public int IncrementalUnrelatedEditBytes()
    {
        EnsurePrepared();
        _edits = 1 - _edits;
        var updated = ShapeCompilations.WithAppendedMember(
            _baseCompilation,
            "FanRoot.cs",
            $"public int UnrelatedEdit{_edits} {{ get; set; }}"
        );
        _driver = _driver.RunGenerators(updated);
        return GeneratorStepTracking.TotalSourceBytes(_driver.GetRunResult().Results.Single());
    }
}

/// <summary>Keyed structural collection benchmarks: a root with K keyed list members.</summary>
[MemoryDiagnoser]
public class GeneratorKeyedCollectionBenchmarks
{
    [Params(1, 4, 16)]
    public int KeyedMembers { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _driver = null!;
    private int _edits;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == KeyedMembers && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = ShapeCompilations.CreateKeyed(KeyedMembers);
        _driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _edits = 0;
        _preparedFor = KeyedMembers;
    }

    [Benchmark(Description = "Generator cold: keyed structural collections")]
    public int ColdGenerationBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator incremental: unrelated edit on a keyed root")]
    public int IncrementalUnrelatedEditBytes()
    {
        EnsurePrepared();
        _edits = 1 - _edits;
        var updated = ShapeCompilations.WithAppendedMember(
            _baseCompilation,
            "KeyedRoot.cs",
            $"public int UnrelatedEdit{_edits} {{ get; set; }}"
        );
        _driver = _driver.RunGenerators(updated);
        return GeneratorStepTracking.TotalSourceBytes(_driver.GetRunResult().Results.Single());
    }
}

/// <summary>Dictionary member benchmarks: a root with M dictionary members.</summary>
[MemoryDiagnoser]
public class GeneratorDictionaryMemberBenchmarks
{
    [Params(1, 8, 32)]
    public int DictionaryMembers { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _driver = null!;
    private int _edits;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == DictionaryMembers && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = ShapeCompilations.CreateDictionary(DictionaryMembers);
        _driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _edits = 0;
        _preparedFor = DictionaryMembers;
    }

    [Benchmark(Description = "Generator cold: dictionary members")]
    public int ColdGenerationBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator incremental: unrelated edit on a dictionary root")]
    public int IncrementalUnrelatedEditBytes()
    {
        EnsurePrepared();
        _edits = 1 - _edits;
        var updated = ShapeCompilations.WithAppendedMember(
            _baseCompilation,
            "DictRoot.cs",
            $"public int UnrelatedEdit{_edits} {{ get; set; }}"
        );
        _driver = _driver.RunGenerators(updated);
        return GeneratorStepTracking.TotalSourceBytes(_driver.GetRunResult().Results.Single());
    }
}

/// <summary>Promoted-model count benchmarks: P distinct promoted partials shared by roots.</summary>
[MemoryDiagnoser]
public class GeneratorPromotedCountBenchmarks
{
    [Params(1, 10, 50)]
    public int PromotedCount { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _driver = null!;
    private GeneratorDriver _sharedDriver = null!;
    private int _edits;
    private int _sharedEdits;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == PromotedCount && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = ShapeCompilations.CreatePromoted(PromotedCount, 3);
        _driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _sharedDriver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _edits = 0;
        _sharedEdits = 0;
        _preparedFor = PromotedCount;
    }

    [Benchmark(Description = "Generator cold: promoted-model count")]
    public int ColdGenerationBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator incremental: unrelated edit beside promoted models")]
    public int IncrementalUnrelatedEditBytes()
    {
        EnsurePrepared();
        _edits = 1 - _edits;
        var updated = ShapeCompilations.WithAppendedMember(
            _baseCompilation,
            "PromotedRoot0.cs",
            $"public int UnrelatedEdit{_edits} {{ get; set; }}"
        );
        _driver = _driver.RunGenerators(updated);
        return GeneratorStepTracking.TotalSourceBytes(_driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator incremental: edit one shared promoted type")]
    public int IncrementalSharedEditBytes()
    {
        EnsurePrepared();
        _sharedEdits = 1 - _sharedEdits;
        var updated = ShapeCompilations.WithAppendedMember(
            _baseCompilation,
            "PromotedShared0.cs",
            $"public string SharedEdit{_sharedEdits} {{ get; set; }} = \"\";"
        );
        _sharedDriver = _sharedDriver.RunGenerators(updated);
        return GeneratorStepTracking.TotalSourceBytes(
            _sharedDriver.GetRunResult().Results.Single()
        );
    }
}

internal static class ShapeCompilations
{
    internal static CSharpCompilation CreateWide(int rootCount, int propertyCount)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var root = 0; root < rootCount; root++)
        {
            var members = string.Join(
                "\n",
                Enumerable
                    .Range(0, propertyCount)
                    .Select(index =>
                        index % 2 == 0
                            ? $"        public string Prop{index} {{ get; set; }} = \"\";"
                            : $"        public int Prop{index} {{ get; set; }}"
                    )
            );
            files[$"WideRoot{root}.cs"] = $$"""
                using SparseFragments;
                [SparseFragmentModel]
                public partial class WideRoot{{root}}
                {
                {{members}}
                }
                """;
        }

        return CreateCompilation(files);
    }

    internal static CSharpCompilation CreateDeep(int depth)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var level = depth - 1; level >= 0; level--)
        {
            var child =
                level == depth - 1
                    ? string.Empty
                    : $"\n        public DepthLevel{level + 1} Child {{ get; set; }} = new();";
            files[$"DepthLevel{level}.cs"] = $$"""
                using SparseFragments;
                public partial class DepthLevel{{level}}
                {
                    public string Label { get; set; } = "";{{child}}
                }
                """;
        }

        files["DeepRoot.cs"] = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class DeepRoot
            {
                public string Label { get; set; } = "";
                public DepthLevel0 Child { get; set; } = new();
            }
            """;
        return CreateCompilation(files);
    }

    internal static CSharpCompilation CreateFanOut(int fanOut)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < fanOut; index++)
        {
            files[$"FanChild{index}.cs"] = $$"""
                using SparseFragments;
                public partial class FanChild{{index}}
                {
                    public string Name { get; set; } = "";
                    public int Value { get; set; }
                }
                """;
        }

        var members = string.Join(
            "\n",
            Enumerable
                .Range(0, fanOut)
                .Select(index =>
                    $"        public FanChild{index} Child{index} {{ get; set; }} = new();"
                )
        );
        files["FanRoot.cs"] = $$"""
            using SparseFragments;
            [SparseFragmentModel]
            public partial class FanRoot
            {
                public string Label { get; set; } = "";
            {{members}}
            }
            """;
        return CreateCompilation(files);
    }

    internal static CSharpCompilation CreateKeyed(int memberCount)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KeyedItem.cs"] = """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class KeyedItem
                {
                    [SparseKey]
                    public string Id { get; set; } = "";
                    public string Name { get; set; } = "";
                    public int Value { get; set; }
                }
                """,
        };
        var members = string.Join(
            "\n",
            Enumerable
                .Range(0, memberCount)
                .Select(index =>
                    $"        public System.Collections.Generic.List<KeyedItem> Keyed{index} {{ get; set; }} = new();"
                )
        );
        files["KeyedRoot.cs"] = $$"""
            using SparseFragments;
            [SparseFragmentModel]
            public partial class KeyedRoot
            {
                public string Label { get; set; } = "";
            {{members}}
            }
            """;
        return CreateCompilation(files);
    }

    internal static CSharpCompilation CreateDictionary(int memberCount)
    {
        var members = string.Join(
            "\n",
            Enumerable
                .Range(0, memberCount)
                .Select(index =>
                    $"        public System.Collections.Generic.Dictionary<string, int> Scores{index} {{ get; set; }} = new();"
                )
        );
        return CreateCompilation(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DictRoot.cs"] = $$"""
                using SparseFragments;
                [SparseFragmentModel]
                public partial class DictRoot
                {
                    public string Label { get; set; } = "";
                {{members}}
                }
                """,
            }
        );
    }

    internal static CSharpCompilation CreatePromoted(int promotedCount, int rootsPerPromoted)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var shared = 0; shared < promotedCount; shared++)
        {
            files[$"PromotedShared{shared}.cs"] = $$"""
                using SparseFragments;
                public partial class PromotedShared{{shared}}
                {
                    public string A { get; set; } = "";
                    public string B { get; set; } = "";
                }
                """;
            for (var root = 0; root < rootsPerPromoted; root++)
            {
                var name = $"PromotedRoot{shared * rootsPerPromoted + root}";
                files[$"{name}.cs"] = $$"""
                    using SparseFragments;
                    [SparseFragmentModel]
                    public partial class {{name}}
                    {
                        public string Label { get; set; } = "";
                        public PromotedShared{{shared}} Child { get; set; } = new();
                    }
                    """;
            }
        }

        return CreateCompilation(files);
    }

    internal static CSharpCompilation WithAppendedMember(
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

    internal static CSharpCompilation CreateCompilation(Dictionary<string, string> files)
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
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        return CSharpCompilation.Create(
            "SparseGeneratorShapeProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }
}
