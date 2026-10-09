using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments;
using SparseFragments.CompilerServices;
using SparseFragments.Generator;

[SparseFragmentModel]
public partial class CertBenchLeaf
{
    public string Label { get; set; } = string.Empty;

    public int Counter { get; set; }

    public List<int> Values { get; set; } = new();
}

[SparseFragmentModel]
public partial class CertBenchKeyedItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class CertBenchKeyedHolder
{
    public string Label { get; set; } = string.Empty;

    public List<CertBenchKeyedItem> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class CertBenchDictHolder
{
    public string Label { get; set; } = string.Empty;

    public Dictionary<string, string> Entries { get; set; } = new();

    public List<int> Values { get; set; } = new();
}

/// <summary>
/// Operation matrix for the three-layer certification (issue #189): Clone,
/// comparison, EditSession, keyed/dictionary, and Patch/ChangeSet paths over
/// small generated models at two collection sizes. Each benchmark returns a
/// checksum so the measured call cannot be eliminated; allocations are
/// recorded by <c>MemoryDiagnoser</c>. Machine-dependent timings are reported
/// in docs/benchmarks/three-layer-certification.md, never asserted.
/// </summary>
[MemoryDiagnoser]
public class ThreeLayerOpsBenchmarks
{
    [Params(64, 1024)]
    public int Size { get; set; }

    private CertBenchLeaf _leaf = null!;
    private CertBenchLeaf.Fragment _leafFragment = default;
    private SparseFragments.Optional<CertBenchLeaf.Fragment?> _leafBefore = default!;
    private SparseFragments.Optional<CertBenchLeaf.Fragment?> _leafAfter = default!;
    private CertBenchLeaf.ChangeSet _leafChange = null!;
    private CertBenchLeaf.ChangeSet _leafNext = null!;
    private CertBenchKeyedHolder _keyedPrototype = null!;
    private SparseFragments.Optional<CertBenchKeyedHolder.Fragment?> _keyedBefore = default!;
    private SparseFragments.Optional<CertBenchKeyedHolder.Fragment?> _keyedAfter = default!;
    private CertBenchKeyedHolder.Patch _keyedPatch = null!;
    private CertBenchDictHolder _dictPrototype = null!;
    private SparseFragments.Optional<CertBenchDictHolder.Fragment?> _dictBefore = default!;
    private SparseFragments.Optional<CertBenchDictHolder.Fragment?> _dictAfter = default!;
    private CertBenchDictHolder.Patch _dictPatch = null!;

    [GlobalSetup]
    public void Setup()
    {
        _leaf = new CertBenchLeaf
        {
            Label = "leaf",
            Counter = 1,
            Values = Enumerable.Range(0, Size).ToList(),
        };
        _leafFragment = CertBenchLeaf.Fragment.From(_leaf);
        _leafBefore = SparseFragments.Optional<CertBenchLeaf.Fragment?>.Present(_leafFragment);
        _leafAfter = SparseFragments.Optional<CertBenchLeaf.Fragment?>.Present(
            CertBenchLeaf.Fragment.From(
                new CertBenchLeaf
                {
                    Label = "leaf-edited",
                    Counter = 2,
                    Values = Enumerable.Range(1, Size).ToList(),
                }
            )
        );
        _leafChange = CertBenchLeaf.ChangeSet.Between(_leafBefore, _leafAfter);
        _leafNext = CertBenchLeaf.ChangeSet.Between(
            _leafAfter,
            SparseFragments.Optional<CertBenchLeaf.Fragment?>.Present(
                CertBenchLeaf.Fragment.From(
                    new CertBenchLeaf
                    {
                        Label = "leaf-final",
                        Counter = 3,
                        Values = Enumerable.Range(2, Size).ToList(),
                    }
                )
            )
        );
        _keyedPrototype = new CertBenchKeyedHolder
        {
            Label = "keyed",
            Items = Enumerable
                .Range(0, Size)
                .Select(index => new CertBenchKeyedItem
                {
                    Id = "id-" + index,
                    Name = "n-" + index,
                    Count = index,
                })
                .ToList(),
        };
        _keyedBefore = SparseFragments.Optional<CertBenchKeyedHolder.Fragment?>.Present(
            CertBenchKeyedHolder.Fragment.From(_keyedPrototype)
        );
        var keyedEdited = new CertBenchKeyedHolder
        {
            Label = "keyed",
            Items = _keyedPrototype
                .Items.Select(item => new CertBenchKeyedItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Count = item.Count,
                })
                .ToList(),
        };
        keyedEdited.Items[0].Name = "edited";
        keyedEdited.Items.Add(
            new CertBenchKeyedItem
            {
                Id = "id-new",
                Name = "new",
                Count = 0,
            }
        );
        _keyedAfter = SparseFragments.Optional<CertBenchKeyedHolder.Fragment?>.Present(
            CertBenchKeyedHolder.Fragment.From(keyedEdited)
        );
        _keyedPatch = CertBenchKeyedHolder.ChangeSet.Between(_keyedBefore, _keyedAfter).ToPatch();
        _dictPrototype = new CertBenchDictHolder
        {
            Label = "dict",
            Entries = Enumerable
                .Range(0, Size)
                .ToDictionary(index => "k-" + index, index => "v-" + index),
            Values = Enumerable.Range(0, Size).ToList(),
        };
        _dictBefore = SparseFragments.Optional<CertBenchDictHolder.Fragment?>.Present(
            CertBenchDictHolder.Fragment.From(_dictPrototype)
        );
        var dictEdited = new CertBenchDictHolder
        {
            Label = "dict-edited",
            Entries = new Dictionary<string, string>(_dictPrototype.Entries)
            {
                ["k-0"] = "changed",
            },
            Values = new List<int>(_dictPrototype.Values) { Size },
        };
        _dictAfter = SparseFragments.Optional<CertBenchDictHolder.Fragment?>.Present(
            CertBenchDictHolder.Fragment.From(dictEdited)
        );
        _dictPatch = CertBenchDictHolder.ChangeSet.Between(_dictBefore, _dictAfter).ToPatch();
        if (
            _leaf.DeepClone().Values.Count != Size
            || _leafFragment.DeepClone().Counter != 1
            || _leafChange.IsEmpty
            || _keyedPatch is null
            || _dictPatch is null
        )
        {
            throw new InvalidOperationException("Certification fixtures must stay valid.");
        }
    }

    [Benchmark(Description = "Clone: model DeepClone")]
    public int CloneModel() => _leaf.DeepClone().Values.Count + _leaf.DeepClone().Counter;

    [Benchmark(Description = "Clone: fragment DeepClone")]
    public int CloneFragment() =>
        _leafFragment.DeepClone().Counter.GetValueOrDefault()
        + (_leafFragment.DeepClone().Label.GetValueOrDefault()?.Length ?? -1);

    [Benchmark(Description = "Comparison: runtime scalar equality")]
    public bool ComparisonRuntime() =>
        SparseFragmentRuntime.AreEqual("leaf", "leaf")
        && !SparseFragmentRuntime.AreEqual("leaf", "other");

    [Benchmark(Description = "Comparison: runtime sequence equality")]
    public bool ComparisonSequence() =>
        SparseFragmentRuntime.AreSequenceEqual(_leaf.Values, _leaf.Values);

    [Benchmark(Description = "Comparison: no-op Between")]
    public bool ComparisonNoopBetween() =>
        CertBenchLeaf.ChangeSet.Between(_leafBefore, _leafBefore).IsEmpty;

    [Benchmark(Description = "EditSession: 64 edits without batching")]
    public int EditSessionIndividual()
    {
        var model = FreshKeyed();
        var session = model.CreateEditSession();
        for (var index = 0; index < 64; index++)
        {
            session.Observable.Items[index % Size].Name = "edit-" + index;
        }

        return model.Items.Count + model.Label.Length;
    }

    [Benchmark(Description = "EditSession: 64 edits in one BatchEdit")]
    public int EditSessionBatched()
    {
        var model = FreshKeyed();
        var session = model.CreateEditSession();
        session.BatchEdit(() =>
        {
            for (var index = 0; index < 64; index++)
            {
                session.Observable.Items[index % Size].Name = "edit-" + index;
            }
        });

        return model.Items.Count + model.Label.Length;
    }

    [Benchmark(Description = "Keyed: Between one edit plus one add")]
    public int KeyedBetween() =>
        CertBenchKeyedHolder.ChangeSet.Between(_keyedBefore, _keyedAfter).ToPatch().GetHashCode();

    [Benchmark(Description = "Keyed: patch apply")]
    public int KeyedApply() =>
        _keyedPatch.Apply(_keyedBefore).Value!.Items.GetValueOrDefault()?.Count ?? -1;

    [Benchmark(Description = "Dictionary: Between edit plus append")]
    public int DictionaryBetween() =>
        CertBenchDictHolder.ChangeSet.Between(_dictBefore, _dictAfter).ToPatch().GetHashCode();

    [Benchmark(Description = "Dictionary: patch apply")]
    public int DictionaryApply() =>
        _dictPatch.Apply(_dictBefore).Value!.Entries.GetValueOrDefault()?.Count ?? -1;

    [Benchmark(Description = "Patch/ChangeSet: ToPatch then apply")]
    public int PatchRoundTrip() =>
        _leafChange.ToPatch().Apply(_leafBefore).Value!.Counter.GetValueOrDefault();

    [Benchmark(Description = "Patch/ChangeSet: invert then apply")]
    public int ChangeSetInvert() =>
        _leafChange.Invert().ToPatch().Apply(_leafAfter).Value!.Counter.GetValueOrDefault();

    [Benchmark(Description = "Patch/ChangeSet: compose then apply")]
    public int ChangeSetCompose() =>
        _leafChange
            .Compose(_leafNext)
            .ToPatch()
            .Apply(_leafBefore)
            .Value!.Counter.GetValueOrDefault();

    private CertBenchKeyedHolder FreshKeyed() =>
        new()
        {
            Label = _keyedPrototype.Label,
            Items = _keyedPrototype
                .Items.Select(item => new CertBenchKeyedItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Count = item.Count,
                })
                .ToList(),
        };
}

/// <summary>
/// Generator-size matrix for the three-layer certification (issue #189):
/// total generated bytes over 1 / 4 / 16 collection models (shared helpers
/// stay constant, so growth is sublinear), plus one-file incremental edits.
/// Returns byte counts, which are deterministic; wall-clock time is reported
/// separately in docs/benchmarks/three-layer-certification.md.
/// </summary>
[MemoryDiagnoser]
public class ThreeLayerGeneratorBenchmarks
{
    [Params(1, 4, 16)]
    public int ModelCount { get; set; }

    private CSharpCompilation _baseCompilation = null!;
    private GeneratorDriver _driver = null!;
    private CSharpCompilation _compilation = null!;
    private int _edits;
    private int _preparedFor = -1;

    [GlobalSetup]
    public void Setup() => EnsurePrepared();

    private void EnsurePrepared()
    {
        if (_preparedFor == ModelCount && _baseCompilation is not null)
        {
            return;
        }

        _baseCompilation = CreateCollectionCompilation(ModelCount);
        _compilation = _baseCompilation;
        _driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(_baseCompilation);
        _edits = 0;
        _preparedFor = ModelCount;
    }

    [Benchmark(Description = "Generator cold: collection models total bytes")]
    public int ColdGenerationBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        return GeneratorStepTracking.TotalSourceBytes(driver.GetRunResult().Results.Single());
    }

    [Benchmark(Description = "Generator cold: shared helper bytes only")]
    public int ColdSharedBytes()
    {
        EnsurePrepared();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(_baseCompilation);
        var result = driver.GetRunResult().Results.Single();
        return result
            .GeneratedSources.Where(static source =>
                source.HintName.Contains("CloneKernels", StringComparison.Ordinal)
                || source.HintName.Contains("ReadOnlyAdapters", StringComparison.Ordinal)
                || source.HintName.Contains("RemovalIndex", StringComparison.Ordinal)
            )
            .Sum(static source => source.SourceText.ToString().Length);
    }

    [Benchmark(Description = "Generator incremental: unrelated edit total bytes")]
    public int IncrementalUnrelatedEditBytes()
    {
        EnsurePrepared();
        _edits++;
        var oldTree = _compilation.SyntaxTrees.First(tree =>
            tree.FilePath.EndsWith("CertModel0.cs", StringComparison.Ordinal)
        );
        var updated = (CSharpCompilation)
            _compilation.ReplaceSyntaxTree(
                oldTree,
                CSharpSyntaxTree.ParseText(
                    oldTree.ToString() + $"\n// edit {_edits}\n",
                    path: "CertModel0.cs"
                )
            );
        _driver = _driver.RunGenerators(updated);
        _compilation = updated;
        return GeneratorStepTracking.TotalSourceBytes(_driver.GetRunResult().Results.Single());
    }

    private static CSharpCompilation CreateCollectionCompilation(int modelCount)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < modelCount; index++)
        {
            files[$"CertModel{index}.cs"] = $$"""
                using SparseFragments;
                using System.Collections.Generic;
                namespace CertBench
                {
                    [SparseFragmentModel]
                    public partial class CertModel{{index}}
                    {
                        public string Label { get; set; } = "";
                        public List<string> Tags { get; set; } = new();
                    }
                }
                """;
        }

        var trees = files
            .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
            .ToArray();
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        return CSharpCompilation.Create(
            "CertBenchGenerator",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
