using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

/// <summary>
/// Incremental-generator regression tests for the promoted-model pipeline
/// (issue #18). Covers the scale matrix from the issue — many independent
/// roots, many roots sharing one promoted partial type, an edit to a single
/// unrelated root, and an edit to the shared promoted type — using Roslyn
/// incremental tracking to observe which stages rerun.
/// </summary>
public sealed class PromotedIncrementalTests
{
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

        public ImmutableArray<Diagnostic> SpfDiagnostics() =>
            DriverResult
                .Diagnostics.Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
                .ToImmutableArray();
    }

    private static CSharpCompilation CreateCompilation(Dictionary<string, string> files)
    {
        var trees = files
            .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
            .ToArray();
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = tpa.Select(path =>
                (MetadataReference)MetadataReference.CreateFromFile(path)
            )
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        return CSharpCompilation.Create(
            "SparsePromotedInvalidationProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(
                NullableContextOptions.Enable
            )
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

    private static IReadOnlyList<IncrementalStepRunReason> TrackedReasons(
        GeneratorRunResult result,
        string trackingName
    ) =>
        result.TrackedSteps.TryGetValue(trackingName, out var steps)
            ? steps.SelectMany(static step => step.Outputs.Select(static output => output.Reason)).ToArray()
            : Array.Empty<IncrementalStepRunReason>();

    private static string SharedSource(string typeName = "InvShared") =>
        $$"""
        using SparseFragments;
        public partial class {{typeName}}
        {
            public string A { get; set; } = "";
            public string B { get; set; } = "";
        }
        """;

    private static string RootSource(string rootName, string sharedName = "InvShared") =>
        $$"""
        using SparseFragments;
        [SparseFragmentModel]
        public partial class {{rootName}}
        {
            public string Label { get; set; } = "";
            public {{sharedName}} Child { get; set; } = new();
        }
        """;

    private static string RootSourceWithExtra(string rootName, string extraMember) =>
        $$"""
        using SparseFragments;
        [SparseFragmentModel]
        public partial class {{rootName}}
        {
            public string Label { get; set; } = "";
            public InvShared Child { get; set; } = new();
            {{extraMember}}
        }
        """;

    private static string PromotedHintFor(Dictionary<string, string> sources, string typeName)
    {
        var match = sources.Keys.SingleOrDefault(key =>
            key.Contains(typeName, StringComparison.Ordinal)
            && key.EndsWith(".SparsePromoted.g.cs", StringComparison.Ordinal)
        );
        match.ShouldNotBeNull($"expected a promoted source for '{typeName}'");
        return match;
    }

    [Test]
    public void UnrelatedRootEdit_PreservesPromotedOutput()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shared.cs"] = SharedSource(),
            ["Root1.cs"] = RootSource("InvRoot1"),
            ["Root2.cs"] = RootSource("InvRoot2"),
            ["Root3.cs"] = RootSource("InvRoot3"),
        };
        var before = Run(files);
        before.SpfDiagnostics().ShouldBeEmpty();
        var beforeSources = before.Sources();
        var promotedHint = PromotedHintFor(beforeSources, "InvShared");

        var after = Rerun(
            before,
            "Root1.cs",
            RootSourceWithExtra("InvRoot1", "public int EditMarker { get; set; }")
        );
        after.SpfDiagnostics().ShouldBeEmpty();
        var afterSources = after.Sources();

        // Same hint set: no promoted file is added or removed.
        afterSources.Keys.OrderBy(static key => key).ShouldBe(beforeSources.Keys.OrderBy(static key => key));
        // The shared promoted output is byte-identical.
        afterSources[promotedHint].ShouldBe(beforeSources[promotedHint]);
        // Untouched roots are byte-identical; the edited root reflects the edit.
        // Each explicit root emits a surface file plus its relocated
        // Patch/ChangeSet operations file (issue #194).
        foreach (var root in new[] { "InvRoot2", "InvRoot3" })
        {
            var hints = afterSources.Keys.Where(key => key.Contains(root, StringComparison.Ordinal)).ToArray();
            hints.Length.ShouldBe(2);
            foreach (var hint in hints)
            {
                afterSources[hint].ShouldBe(beforeSources[hint]);
            }
        }
        var editedHints = afterSources.Keys.Where(key =>
            key.Contains("InvRoot1", StringComparison.Ordinal)
        ).ToArray();
        editedHints.Length.ShouldBe(2);
        string.Concat(editedHints.Select(hint => afterSources[hint])).ShouldContain("EditMarker");
    }

    [Test]
    public void UnrelatedRootEdit_PromotedEmitStageStaysCached()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shared.cs"] = SharedSource(),
            ["Root1.cs"] = RootSource("InvRoot1"),
            ["Root2.cs"] = RootSource("InvRoot2"),
        };
        var before = Run(files);
        before.SpfDiagnostics().ShouldBeEmpty();

        var after = Rerun(
            before,
            "Root1.cs",
            RootSourceWithExtra("InvRoot1", "public int EditMarker { get; set; }")
        );

        // The keyed promoted aggregation isolates the edit: dedup and emit
        // inputs are value-equal, so those stages must not recompute.
        foreach (var stage in new[] { "SparseFragmentsGenerator.PromotedDedup", "SparseFragmentsGenerator.Promoted" })
        {
            var reasons = TrackedReasons(after.GeneratorResult, stage);
            reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'");
            reasons
                .All(static reason => reason == IncrementalStepRunReason.Cached)
                .ShouldBeTrue(
                    $"expected '{stage}' to stay cached after an unrelated edit, got: {string.Join(", ", reasons)}"
                );
        }
    }

    [Test]
    public void SharedPromotedEdit_UpdatesPromotedOutputOnly()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shared.cs"] = SharedSource(),
            ["Root1.cs"] = RootSource("InvRoot1"),
            ["Root2.cs"] = RootSource("InvRoot2"),
        };
        var before = Run(files);
        before.SpfDiagnostics().ShouldBeEmpty();
        var beforeSources = before.Sources();
        var promotedHint = PromotedHintFor(beforeSources, "InvShared");

        const string editedShared = """
            using SparseFragments;
            public partial class InvShared
            {
                public string A { get; set; } = "";
                public string B { get; set; } = "";
                public string C { get; set; } = "";
            }
            """;
        var after = Rerun(before, "Shared.cs", editedShared);
        after.SpfDiagnostics().ShouldBeEmpty();
        var afterSources = after.Sources();

        // Hint set is stable (deterministic hint names) but the promoted
        // content follows the shared type edit.
        afterSources.Keys.OrderBy(static key => key).ShouldBe(beforeSources.Keys.OrderBy(static key => key));
        afterSources[promotedHint].ShouldNotBe(beforeSources[promotedHint]);
        afterSources[promotedHint].ShouldContain("C");

        // Root outputs only reference the promoted fragment by name.
        // Each explicit root emits a surface file plus its relocated
        // Patch/ChangeSet operations file (issue #194).
        foreach (var root in new[] { "InvRoot1", "InvRoot2" })
        {
            var hints = afterSources.Keys.Where(key => key.Contains(root, StringComparison.Ordinal)).ToArray();
            hints.Length.ShouldBe(2);
            foreach (var hint in hints)
            {
                afterSources[hint].ShouldBe(beforeSources[hint]);
            }
        }

        // The shared edit must invalidate the promoted emit stage.
        var reasons = TrackedReasons(after.GeneratorResult, "SparseFragmentsGenerator.Promoted");
        reasons.ShouldNotBeEmpty();
        reasons
            .Any(static reason => reason != IncrementalStepRunReason.Cached)
            .ShouldBeTrue(
                $"expected promoted emit to recompute after a shared-type edit, got: {string.Join(", ", reasons)}"
            );
    }

    [Test]
    public void ExplicitRoot_SuppressesPromotedDuplicate()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shared.cs"] =
                """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class InvExplicitShared
                {
                    public string A { get; set; } = "";
                }
                """,
            ["Root1.cs"] = RootSource("InvExplicitRoot1", "InvExplicitShared"),
            ["Root2.cs"] = RootSource("InvExplicitRoot2", "InvExplicitShared"),
        };
        var probe = Run(files);
        probe.SpfDiagnostics().ShouldBeEmpty();
        var sources = probe.Sources();

        // The shared type gets exactly one explicit-root surface file plus
        // its relocated operations file, never a promoted duplicate.
        var explicitHints = sources.Keys.Where(key => key.Contains("InvExplicitShared", StringComparison.Ordinal)).ToArray();
        explicitHints.Length.ShouldBe(2);
        explicitHints.Any(static key => key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal)).ShouldBeTrue();
        explicitHints.Any(static key => key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal)).ShouldBeTrue();
        sources.Keys.Any(key => key.EndsWith(".SparsePromoted.g.cs", StringComparison.Ordinal)).ShouldBeFalse();
        sources.Keys.Any(key => key.Contains("InvExplicitRoot1", StringComparison.Ordinal)).ShouldBeTrue();
        sources.Keys.Any(key => key.Contains("InvExplicitRoot2", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Test]
    public void ManyRoots_SharingOnePromotedType_GenerateDeterministically()
    {
        const int rootCount = 50;
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Shared.cs"] = SharedSource("InvScaleShared"),
        };
        for (var index = 0; index < rootCount; index++)
        {
            files[$"Root{index}.cs"] = RootSource($"InvScaleRoot{index}", "InvScaleShared");
        }

        var first = Run(files);
        first.SpfDiagnostics().ShouldBeEmpty();
        var second = Run(files);
        second.SpfDiagnostics().ShouldBeEmpty();

        // Deterministic across runs: identical hint sets and identical bytes.
        var firstSources = first.Sources();
        var secondSources = second.Sources();
        secondSources.Keys.OrderBy(static key => key).ShouldBe(firstSources.Keys.OrderBy(static key => key));
        foreach (var hint in firstSources.Keys)
        {
            secondSources[hint].ShouldBe(firstSources[hint]);
        }

        // One promoted file shared by all roots, plus one file per root.
        firstSources.Keys.Count(key => key.EndsWith(".SparsePromoted.g.cs", StringComparison.Ordinal)).ShouldBe(1);
        var promotedHint = PromotedHintFor(firstSources, "InvScaleShared");

        // An unrelated single-root edit preserves the shared promoted bytes.
        var edited = Rerun(
            first,
            "Root7.cs",
            $$"""
            using SparseFragments;
            [SparseFragmentModel]
            public partial class InvScaleRoot7
            {
                public string Label { get; set; } = "";
                public InvScaleShared Child { get; set; } = new();
                public int EditMarker { get; set; }
            }
            """
        );
        edited.SpfDiagnostics().ShouldBeEmpty();
        var editedSources = edited.Sources();
        editedSources[promotedHint].ShouldBe(firstSources[promotedHint]);
    }

    [Test]
    public void LargeProject_ManyIndependentRoots_MeetsEnvelope()
    {
        // Large-project envelope: 100 independent roots must generate
        // completely (cold) and keep unrelated outputs stable across a
        // single-file incremental edit.
        const int rootCount = 100;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < rootCount; index++)
        {
            files[$"Root{index}.cs"] =
                $$"""
                using SparseFragments;
                [SparseFragmentModel]
                public partial class InvEnvelopeRoot{{index}}
                {
                    public string Label { get; set; } = "";
                    public int Index { get; set; } = {{index}};
                }
                """;
        }

        var cold = Run(files);
        cold.SpfDiagnostics().ShouldBeEmpty();
        var coldSources = cold.Sources();
        coldSources
            .Keys.Count(key => key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal))
            .ShouldBe(rootCount);

        var edited = Rerun(
            cold,
            "Root3.cs",
            """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class InvEnvelopeRoot3
            {
                public string Label { get; set; } = "";
                public int Index { get; set; } = 3;
                public int EditMarker { get; set; }
            }
            """
        );
        edited.SpfDiagnostics().ShouldBeEmpty();
        var editedSources = edited.Sources();
        editedSources.Keys.OrderBy(static key => key).ShouldBe(coldSources.Keys.OrderBy(static key => key));
        foreach (var hint in coldSources.Keys.Where(key => !key.Contains("InvEnvelopeRoot3", StringComparison.Ordinal)))
        {
            editedSources[hint].ShouldBe(coldSources[hint]);
        }
    }
}
