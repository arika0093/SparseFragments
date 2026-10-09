using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

/// <summary>
/// Deterministic incremental step-tracking regression tests (issue #61),
/// adjacent to the BenchmarkDotNet generator benchmarks. Records cached vs
/// recomputed outputs for the eleven tracked generator stages on unrelated
/// vs shared edits, and pins model-shape dimensions (width, nesting
/// depth/fan-out, keyed collections, dictionary members, promoted-model
/// count) with generated source size observability. Wall-clock timing stays in
/// benchmarks; these tests pin invalidation behavior so follow-up optimization
/// can only be justified by measured invalidation.
/// </summary>
public sealed class GeneratorStepTrackingTests
{
    internal static readonly string[] TrackedStageNames =
    [
        "SparseFragmentsGenerator.Analysis",
        "SparseFragmentsGenerator.BclSetSupport",
        "SparseFragmentsGenerator.Output",
        "SparseFragmentsGenerator.PromotedPerRoot",
        "SparseFragmentsGenerator.PromotedContributions",
        "SparseFragmentsGenerator.ExplicitRoots",
        "SparseFragmentsGenerator.PromotedDedup",
        "SparseFragmentsGenerator.PromotedDistinct",
        "SparseFragmentsGenerator.Promoted",
        "SparseFragmentsGenerator.PromotedConflicts",
        "SparseFragmentsGenerator.IsExternalInit",
    ];

    private sealed record Probe(
        GeneratorDriver Driver,
        CSharpCompilation Compilation,
        GeneratorDriverRunResult DriverResult,
        GeneratorRunResult GeneratorResult
    )
    {
        public Dictionary<string, string> Sources() =>
            DriverResult
                .Results.SelectMany(static result => result.GeneratedSources)
                .GroupBy(static source => source.HintName)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First().SourceText.ToString(),
                    StringComparer.Ordinal
                );

        public int TotalSourceBytes() => Sources().Values.Sum(static source => source.Length);

        public int PromotedSourceCount() =>
            Sources()
                .Keys.Count(static key =>
                    key.EndsWith(".SparsePromoted.g.cs", StringComparison.Ordinal)
                );

        public ImmutableArray<Diagnostic> SpfDiagnostics() =>
            DriverResult
                .Diagnostics.Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
                .ToImmutableArray();

        public IReadOnlyList<IncrementalStepRunReason> Reasons(string trackingName) =>
            GeneratorResult.TrackedSteps.TryGetValue(trackingName, out var steps)
                ? steps
                    .SelectMany(static step => step.Outputs.Select(static output => output.Reason))
                    .ToArray()
                : Array.Empty<IncrementalStepRunReason>();

        public string StepTable() =>
            string.Join(
                "\n",
                TrackedStageNames.Select(name =>
                {
                    var reasons = Reasons(name);
                    var cached = reasons.Count(static reason =>
                        reason == IncrementalStepRunReason.Cached
                    );
                    var unchanged = reasons.Count(static reason =>
                        reason == IncrementalStepRunReason.Unchanged
                    );
                    var modified = reasons.Count(static reason =>
                        reason == IncrementalStepRunReason.Modified
                    );
                    var fresh = reasons.Count(static reason =>
                        reason == IncrementalStepRunReason.New
                    );
                    return $"{name}: outputs={reasons.Count}, cached={cached}, unchanged={unchanged}, modified={modified}, new={fresh}";
                })
            );
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
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        return CSharpCompilation.Create(
            "SparseStepTrackingProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static GeneratorDriver CreateTrackedDriver() =>
        CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SparseFragmentsGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );

    private static Probe Run(Dictionary<string, string> files)
    {
        var compilation = CreateCompilation(files);
        var driver = CreateTrackedDriver().RunGenerators(compilation);
        var driverResult = driver.GetRunResult();
        return new Probe(driver, compilation, driverResult, driverResult.Results.Single());
    }

    private static Probe Rerun(Probe probe, string path, string newText)
    {
        var oldTree = probe.Compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
        var newTree = CSharpSyntaxTree.ParseText(newText, path: path);
        var compilation = (CSharpCompilation)probe.Compilation.ReplaceSyntaxTree(oldTree, newTree);
        var driver = probe.Driver.RunGenerators(compilation);
        var driverResult = driver.GetRunResult();
        return new Probe(driver, compilation, driverResult, driverResult.Results.Single());
    }

    private static string SharedSource() =>
        """
            using SparseFragments;
            public partial class TrackShared
            {
                public string A { get; set; } = "";
                public string B { get; set; } = "";
            }
            """;

    private static string RootSource(string rootName) =>
        $$"""
            using SparseFragments;
            [SparseFragmentModel]
            public partial class {{rootName}}
            {
                public string Label { get; set; } = "";
                public TrackShared Child { get; set; } = new();
            }
            """;

    private static string RootSourceWithExtra(string rootName, string extraMember) =>
        $$"""
            using SparseFragments;
            [SparseFragmentModel]
            public partial class {{rootName}}
            {
                public string Label { get; set; } = "";
                public TrackShared Child { get; set; } = new();
                {{extraMember}}
            }
            """;

    private static Dictionary<string, string> ThreeRootFiles() =>
        new(StringComparer.Ordinal)
        {
            ["Shared.cs"] = SharedSource(),
            ["Root1.cs"] = RootSource("TrackRoot1"),
            ["Root2.cs"] = RootSource("TrackRoot2"),
            ["Root3.cs"] = RootSource("TrackRoot3"),
        };

    private static void AllCached(Probe probe, string stage)
    {
        var reasons = probe.Reasons(stage);
        reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'\n{probe.StepTable()}");
        reasons
            .All(static reason => reason == IncrementalStepRunReason.Cached)
            .ShouldBeTrue(
                $"expected '{stage}' to stay cached, got: {string.Join(", ", reasons)}\n{probe.StepTable()}"
            );
    }

    /// <summary>
    /// Asserts no output changed value: every output is either served from
    /// cache or recomputed value-equal (<c>Unchanged</c>). Compilation-derived
    /// singletons (BclSetSupport, IsExternalInit) rerun on
    /// every compilation but stay value-equal; shared-type edits likewise
    /// leave per-root outputs value-equal because roots reference promoted
    /// fragments by name only.
    /// </summary>
    private static void NoneModified(Probe probe, string stage)
    {
        var reasons = probe.Reasons(stage);
        reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'\n{probe.StepTable()}");
        reasons
            .All(static reason =>
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged
            )
            .ShouldBeTrue(
                $"expected '{stage}' to change no output, got: {string.Join(", ", reasons)}\n{probe.StepTable()}"
            );
    }

    private static void HasRecomputed(Probe probe, string stage)
    {
        var reasons = probe.Reasons(stage);
        reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'\n{probe.StepTable()}");
        reasons
            .Any(static reason => reason != IncrementalStepRunReason.Cached)
            .ShouldBeTrue(
                $"expected '{stage}' to recompute, got: {string.Join(", ", reasons)}\n{probe.StepTable()}"
            );
    }

    private static void HasModified(Probe probe, string stage)
    {
        var reasons = probe.Reasons(stage);
        reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'\n{probe.StepTable()}");
        reasons
            .Any(static reason => reason == IncrementalStepRunReason.Modified)
            .ShouldBeTrue(
                $"expected '{stage}' to change an output, got: {string.Join(", ", reasons)}\n{probe.StepTable()}"
            );
    }

    private static void IsMixed(Probe probe, string stage)
    {
        var reasons = probe.Reasons(stage);
        reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'\n{probe.StepTable()}");
        reasons
            .Any(static reason => reason == IncrementalStepRunReason.Cached)
            .ShouldBeTrue(
                $"expected '{stage}' to keep cached outputs, got: {string.Join(", ", reasons)}\n{probe.StepTable()}"
            );
        reasons
            .Any(static reason => reason != IncrementalStepRunReason.Cached)
            .ShouldBeTrue(
                $"expected '{stage}' to recompute some outputs, got: {string.Join(", ", reasons)}\n{probe.StepTable()}"
            );
    }

    [Test]
    public void ColdRun_TracksAllTwelveStages()
    {
        var probe = Run(ThreeRootFiles());
        probe.SpfDiagnostics().ShouldBeEmpty();

        // PromotedConflicts only appears in tracking when incompatible promoted
        // models exist (SPF010 is defensive-only: the same fully-qualified name
        // always yields identical semantics), so it is expected-but-optional.
        foreach (var stage in TrackedStageNames)
        {
            if (stage == "SparseFragmentsGenerator.PromotedConflicts")
            {
                continue;
            }

            probe.GeneratorResult.TrackedSteps.ShouldContainKey(stage);
        }
    }

    [Test]
    public void UnrelatedRootEdit_CachesPromotedStages()
    {
        var before = Run(ThreeRootFiles());
        before.SpfDiagnostics().ShouldBeEmpty();
        var after = Rerun(
            before,
            "Root1.cs",
            RootSourceWithExtra("TrackRoot1", "public int EditMarker { get; set; }")
        );
        after.SpfDiagnostics().ShouldBeEmpty();

        // The edited root flows through analysis and output, but every
        // promoted aggregation stage keeps value-equal inputs and stays cached.
        // Compilation-derived singletons rerun value-equal (Unchanged).
        HasModified(after, "SparseFragmentsGenerator.Analysis");
        IsMixed(after, "SparseFragmentsGenerator.Output");
        IsMixed(after, "SparseFragmentsGenerator.PromotedPerRoot");
        IsMixed(after, "SparseFragmentsGenerator.PromotedContributions");
        foreach (
            var stage in new[]
            {
                "SparseFragmentsGenerator.ExplicitRoots",
                "SparseFragmentsGenerator.PromotedDedup",
                "SparseFragmentsGenerator.PromotedDistinct",
                "SparseFragmentsGenerator.Promoted",
            }
        )
        {
            AllCached(after, stage);
        }

        foreach (
            var stage in new[]
            {
                "SparseFragmentsGenerator.IsExternalInit",
                "SparseFragmentsGenerator.BclSetSupport",
            }
        )
        {
            NoneModified(after, stage);
        }
    }

    [Test]
    public void SharedPromotedEdit_RecomputesPromotedStagesButNotRootOutputs()
    {
        var before = Run(ThreeRootFiles());
        before.SpfDiagnostics().ShouldBeEmpty();
        const string editedShared = """
            using SparseFragments;
            public partial class TrackShared
            {
                public string A { get; set; } = "";
                public string B { get; set; } = "";
                public string C { get; set; } = "";
            }
            """;
        var after = Rerun(before, "Shared.cs", editedShared);
        after.SpfDiagnostics().ShouldBeEmpty();

        // The promoted aggregation follows the shared type edit: every root
        // analysis embeds the shared structure (Modified) and the promoted
        // stages re-emit, while per-root outputs only reference the promoted
        // fragment by name and change no output (recomputed value-equal).
        HasModified(after, "SparseFragmentsGenerator.Analysis");
        HasModified(after, "SparseFragmentsGenerator.PromotedDedup");
        HasModified(after, "SparseFragmentsGenerator.PromotedDistinct");
        HasModified(after, "SparseFragmentsGenerator.Promoted");
        NoneModified(after, "SparseFragmentsGenerator.Output");
        AllCached(after, "SparseFragmentsGenerator.ExplicitRoots");
    }

    [Test]
    public void WideModel_BytesGrowWithPropertyCount()
    {
        static Dictionary<string, string> Wide(int properties)
        {
            var members = string.Join(
                "\n",
                Enumerable
                    .Range(0, properties)
                    .Select(index => $"    public string Prop{index} {{ get; set; }} = \"\";")
            );
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Wide.cs"] = $$"""
                    using SparseFragments;
                    [SparseFragmentModel]
                    public partial class WideModel
                    {
                    {{members}}
                    }
                    """,
            };
        }

        var narrow = Run(Wide(4));
        var wide = Run(Wide(32));
        narrow.SpfDiagnostics().ShouldBeEmpty();
        wide.SpfDiagnostics().ShouldBeEmpty();

        // Deterministic across cold runs and monotonically growing with width.
        Run(Wide(32)).Sources().ShouldBe(wide.Sources());
        wide.TotalSourceBytes().ShouldBeGreaterThan(narrow.TotalSourceBytes());

        // An unrelated edit preserves the hint set.
        var edited = Rerun(
            narrow,
            "Wide.cs",
            Wide(4)["Wide.cs"].Replace("}", "    public int EditMarker { get; set; }\n}")
        );
        edited.SpfDiagnostics().ShouldBeEmpty();
        edited.Sources().Keys.ShouldBe(narrow.Sources().Keys);
    }

    [Test]
    public void NestedDepth_GeneratesOnePromotedFilePerLevel()
    {
        static Dictionary<string, string> Deep(int depth)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var level = depth - 1; level >= 0; level--)
            {
                var child =
                    level == depth - 1
                        ? string.Empty
                        : $"\n    public DepthLevel{level + 1} Child {{ get; set; }} = new();";
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
            return files;
        }

        var probe = Run(Deep(3));
        probe.SpfDiagnostics().ShouldBeEmpty();
        probe.PromotedSourceCount().ShouldBe(3);

        // Deterministic across cold runs; an unrelated root edit preserves promoted bytes.
        var sources = probe.Sources();
        Run(Deep(3)).Sources().ShouldBe(sources);
        var edited = Rerun(
            probe,
            "DeepRoot.cs",
            """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class DeepRoot
            {
                public string Label { get; set; } = "";
                public DepthLevel0 Child { get; set; } = new();
                public int EditMarker { get; set; }
            }
            """
        );
        edited.SpfDiagnostics().ShouldBeEmpty();
        foreach (
            var hint in sources.Keys.Where(static key =>
                key.EndsWith(".SparsePromoted.g.cs", StringComparison.Ordinal)
            )
        )
        {
            edited.Sources()[hint].ShouldBe(sources[hint]);
        }
    }

    [Test]
    public void NestedFanOut_GeneratesOnePromotedFilePerChild()
    {
        const int fanOut = 4;
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
                    $"    public FanChild{index} Child{index} {{ get; set; }} = new();"
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

        var probe = Run(files);
        probe.SpfDiagnostics().ShouldBeEmpty();
        probe.PromotedSourceCount().ShouldBe(fanOut);
        Run(files).Sources().ShouldBe(probe.Sources());
    }

    [Test]
    public void KeyedCollection_GeneratesWithoutDiagnostics()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KeyedItem.cs"] = """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class ShapeKeyedItem
                {
                    [SparseKey]
                    public string Id { get; set; } = "";
                    public string Name { get; set; } = "";
                }
                """,
            ["KeyedRoot.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class ShapeKeyedRoot
                {
                    public string Label { get; set; } = "";
                    public List<ShapeKeyedItem> Items { get; set; } = new();
                }
                """,
        };

        var probe = Run(files);
        probe.SpfDiagnostics().ShouldBeEmpty();
        var sources = probe.Sources();
        sources
            .Keys.Any(static key => key.Contains("ShapeKeyedRoot", StringComparison.Ordinal))
            .ShouldBeTrue();

        // Deterministic across cold runs; an unrelated edit preserves the hint set.
        Run(files).Sources().ShouldBe(sources);
        var edited = Rerun(
            probe,
            "KeyedRoot.cs",
            """
            using SparseFragments;
            using System.Collections.Generic;
            [SparseFragmentModel]
            public partial class ShapeKeyedRoot
            {
                public string Label { get; set; } = "";
                public List<ShapeKeyedItem> Items { get; set; } = new();
                public int EditMarker { get; set; }
            }
            """
        );
        edited.SpfDiagnostics().ShouldBeEmpty();
        edited.Sources().Keys.ShouldBe(sources.Keys);
    }

    [Test]
    public void DictionaryMembers_GenerateWithoutDiagnostics()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DictRoot.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class ShapeDictRoot
                {
                    public string Label { get; set; } = "";
                    public Dictionary<string, int> Scores { get; set; } = new();
                    public Dictionary<string, string> Tags { get; set; } = new();
                }
                """,
        };

        var probe = Run(files);
        probe.SpfDiagnostics().ShouldBeEmpty();
        var sources = probe.Sources();
        Run(files).Sources().ShouldBe(sources);
        probe.TotalSourceBytes().ShouldBeGreaterThan(0);
    }

    [Test]
    public void PromotedCount_InvalidatesOnlyTheEditedSharedType()
    {
        static Dictionary<string, string> Promoted(int count)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var shared = 0; shared < count; shared++)
            {
                files[$"Shared{shared}.cs"] = $$"""
                    using SparseFragments;
                    public partial class ShapeShared{{shared}}
                    {
                        public string A { get; set; } = "";
                    }
                    """;
                var name = $"ShapeRoot{shared}";
                files[$"{name}.cs"] = $$"""
                    using SparseFragments;
                    [SparseFragmentModel]
                    public partial class {{name}}
                    {
                        public string Label { get; set; } = "";
                        public ShapeShared{{shared}} Child { get; set; } = new();
                    }
                    """;
            }

            return files;
        }

        var before = Run(Promoted(3));
        before.SpfDiagnostics().ShouldBeEmpty();
        var beforeSources = before.Sources();
        beforeSources
            .Keys.Count(static key =>
                key.EndsWith(".SparsePromoted.g.cs", StringComparison.Ordinal)
            )
            .ShouldBe(3);

        // An unrelated root edit preserves every promoted file byte-identically.
        var unrelated = Rerun(
            before,
            "ShapeRoot0.cs",
            """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class ShapeRoot0
            {
                public string Label { get; set; } = "";
                public ShapeShared0 Child { get; set; } = new();
                public int EditMarker { get; set; }
            }
            """
        );
        unrelated.SpfDiagnostics().ShouldBeEmpty();
        foreach (
            var hint in beforeSources.Keys.Where(static key =>
                key.EndsWith(".SparsePromoted.g.cs", StringComparison.Ordinal)
            )
        )
        {
            unrelated.Sources()[hint].ShouldBe(beforeSources[hint]);
        }

        // A shared-type edit changes exactly its own promoted file.
        var shared = Rerun(
            before,
            "Shared1.cs",
            """
            using SparseFragments;
            public partial class ShapeShared1
            {
                public string A { get; set; } = "";
                public string B { get; set; } = "";
            }
            """
        );
        shared.SpfDiagnostics().ShouldBeEmpty();
        var sharedSources = shared.Sources();
        sharedSources.Keys.ShouldBe(beforeSources.Keys);
        var changed = beforeSources
            .Keys.Where(key => sharedSources[key] != beforeSources[key])
            .ToArray();
        // Union: the edited shared type changes its promoted surface, its
        // reloc-2 payload/operations implementation file, and its reloc-1
        // UI/session implementations — nothing else.
        changed.Length.ShouldBe(5);
        foreach (var hint in changed)
        {
            hint.ShouldContain("ShapeShared1");
        }
    }
}
