using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

// Generator-driver and cross-assembly acceptance for the
// compilation-scoped emission plane (issue #180): exactly one helper
// definition per compilation, absent-when-unused, deterministic and
// incremental outputs, hidden implementations behind public contracts.
// Downstream-dialect behavior probes live in GeneratedOnceConformanceTests.
public sealed class GeneratedOnceDriverTests
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

        public IReadOnlyList<IncrementalStepRunReason> Reasons(string trackingName) =>
            GeneratorResult.TrackedSteps.TryGetValue(trackingName, out var steps)
                ? steps
                    .SelectMany(static step => step.Outputs.Select(static output => output.Reason))
                    .ToArray()
                : Array.Empty<IncrementalStepRunReason>();
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
            "SparseGeneratedOnceProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static Probe Run(Dictionary<string, string> files, bool tracked = false)
    {
        var compilation = CreateCompilation(files);
        GeneratorDriver driver = tracked
            ? CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new SparseFragmentsGenerator().AsSourceGenerator() },
                driverOptions: new GeneratorDriverOptions(
                    IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true
                )
            )
            : CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
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

    private const string ScalarModel = """
        using SparseFragments;
        namespace OnceProbe
        {
            [SparseFragmentModel]
            public partial class ScalarModel
            {
                public string Label { get; set; } = "";
            }
        }
        """;

    private const string CollectionModel = """
        using SparseFragments;
        using System.Collections.Generic;
        namespace OnceProbe
        {
            [SparseFragmentModel]
            public partial class CollectionModel
            {
                public string Label { get; set; } = "";
                public List<string> Tags { get; set; } = new();
            }
        }
        """;

    private const string KeyedModel = """
        using SparseFragments;
        using System.Collections.Generic;
        namespace OnceProbe
        {
            [SparseFragmentModel]
            public partial class KeyedModel
            {
                [SparseKey]
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class KeyedRoot
            {
                public string Label { get; set; } = "";
                public List<KeyedModel> Items { get; set; } = new();
            }
        }
        """;

    private static int CountTypeDefinitions(Dictionary<string, string> sources, string snippet) =>
        sources.Values.Sum(source =>
            source.Split('\n').Count(line => line.Contains(snippet, StringComparison.Ordinal))
        );

    private static void NoneModified(Probe probe, string stage)
    {
        var reasons = probe.Reasons(stage);
        reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'");
        reasons
            .All(static reason =>
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged
            )
            .ShouldBeTrue(
                $"expected '{stage}' to change no output, got: {string.Join(", ", reasons)}"
            );
    }

    private static void HasModified(Probe probe, string stage)
    {
        var reasons = probe.Reasons(stage);
        reasons.ShouldNotBeEmpty($"expected tracking data for '{stage}'");
        reasons
            .Any(static reason => reason == IncrementalStepRunReason.Modified)
            .ShouldBeTrue(
                $"expected '{stage}' to change an output, got: {string.Join(", ", reasons)}"
            );
    }

    [Test]
    public void NoModels_EmitsNoGeneratedOnceHelpers()
    {
        var probe = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Plain.cs"] = """
                namespace OnceProbe
                {
                    public class Plain { public string Label { get; set; } = ""; }
                }
                """,
            }
        );
        probe.SpfDiagnostics().ShouldBeEmpty();
        probe.Sources().ShouldBeEmpty();
    }

    [Test]
    public void ScalarClass_EmitsOnlyEditSessionCore()
    {
        var probe = Run(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Scalar.cs"] = ScalarModel }
        );
        probe.SpfDiagnostics().ShouldBeEmpty();
        var sources = probe.Sources();
        sources
            .Keys.Count(key => key.EndsWith("EditSessionCore.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
        sources
            .Keys.Count(key =>
                key.EndsWith("EditSessionWithCurrentCore.g.cs", StringComparison.Ordinal)
            )
            .ShouldBe(1);
        sources.Keys.ShouldNotContain(key =>
            key.Contains("CloneHelpers", StringComparison.Ordinal)
            || key.Contains("ReadOnlyAdapters", StringComparison.Ordinal)
            || key.Contains("RemovalIndex", StringComparison.Ordinal)
        );
    }

    [Test]
    public void ManyModelsShareExactlyOneDefinitionPerHelper()
    {
        var probe = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Scalar.cs"] = ScalarModel,
                ["Collection.cs"] = CollectionModel,
                ["Keyed.cs"] = KeyedModel,
                ["SecondCollection.cs"] = CollectionModel.Replace(
                    "CollectionModel",
                    "SecondCollectionModel"
                ),
            }
        );
        probe.SpfDiagnostics().ShouldBeEmpty();
        var sources = probe.Sources();
        CountTypeDefinitions(sources, "class SparseCloneHelpers").ShouldBe(1);
        CountTypeDefinitions(sources, "class SparseReadOnlyListAdapter").ShouldBe(1);
        CountTypeDefinitions(sources, "class SparseReadOnlyDictionaryAdapter<").ShouldBe(1);
        CountTypeDefinitions(sources, "class SparseReadOnlyDictionaryEntriesAdapter").ShouldBe(1);
        CountTypeDefinitions(sources, "class SparseRemovalIndex<").ShouldBe(1);
        CountTypeDefinitions(sources, "class EditSessionCore<").ShouldBe(2);
        sources
            .Keys.Count(key => key.Contains("CloneHelpers", StringComparison.Ordinal))
            .ShouldBe(1);
        sources
            .Keys.Count(key => key.Contains("ReadOnlyAdapters", StringComparison.Ordinal))
            .ShouldBe(1);
        sources
            .Keys.Count(key => key.Contains("RemovalIndex", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Test]
    public void DistinctAndGlobalNamespacesShareOneHelperSet()
    {
        var probe = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["A.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                namespace Alpha
                {
                    [SparseFragmentModel]
                    public partial class Widget
                    {
                        public List<string> Tags { get; set; } = new();
                    }
                }
                """,
                ["B.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                namespace Beta
                {
                    [SparseFragmentModel]
                    public partial class Widget
                    {
                        public List<string> Tags { get; set; } = new();
                    }
                }
                """,
                ["Global.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class GlobalWidget
                {
                    public List<string> Tags { get; set; } = new();
                }
                """,
            }
        );
        probe.SpfDiagnostics().ShouldBeEmpty();
        var sources = probe.Sources();
        CountTypeDefinitions(sources, "class SparseCloneHelpers").ShouldBe(1);
        // Colliding short names still compile through per-model containers.
        var updated = RunAndUpdate(probe.Compilation, probe.Driver);
        updated
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    [Test]
    public void StructOnly_EmitsNoEditSessionCore()
    {
        var probe = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Value.cs"] = """
                using SparseFragments;
                namespace OnceProbe
                {
                    [SparseFragmentModel]
                    public partial struct ValueModel
                    {
                        public string Label { get; set; }
                    }
                }
                """,
            }
        );
        probe.SpfDiagnostics().ShouldBeEmpty();
        probe
            .Sources()
            .Keys.ShouldNotContain(key => key.Contains("EditSession", StringComparison.Ordinal));
    }

    [Test]
    public void PromotedCollectionsContributeCapabilities()
    {
        var probe = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Shared.cs"] = """
                using System.Collections.Generic;
                namespace OnceProbe
                {
                    public partial class SharedPoco
                    {
                        public List<string> Tags { get; set; } = new();
                    }
                }
                """,
                ["Root.cs"] = """
                using SparseFragments;
                namespace OnceProbe
                {
                    [SparseFragmentModel]
                    public partial class PromotedRoot
                    {
                        public string Label { get; set; } = "";
                        public SharedPoco Child { get; set; } = new();
                    }
                }
                """,
            }
        );
        probe.SpfDiagnostics().ShouldBeEmpty();
        var sources = probe.Sources();
        sources
            .Keys.Any(key => key.Contains("CloneHelpers", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    public void OutputsAreDeterministicAcrossColdRuns()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Scalar.cs"] = ScalarModel,
            ["Collection.cs"] = CollectionModel,
            ["Keyed.cs"] = KeyedModel,
        };
        Run(files).Sources().ShouldBe(Run(files).Sources());
    }

    [Test]
    public void UnrelatedEdit_PreservesHelperBytesAndHintSet()
    {
        var before = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Scalar.cs"] = ScalarModel,
                ["Collection.cs"] = CollectionModel,
            },
            tracked: true
        );
        before.SpfDiagnostics().ShouldBeEmpty();
        var beforeSources = before.Sources();
        var after = Rerun(
            before,
            "Scalar.cs",
            ScalarModel.Replace("}", "    public int EditMarker { get; set; }\n}")
        );
        after.SpfDiagnostics().ShouldBeEmpty();
        var afterSources = after.Sources();
        afterSources
            .Keys.OrderBy(static key => key)
            .ShouldBe(beforeSources.Keys.OrderBy(static key => key));
        foreach (
            var hint in beforeSources.Keys.Where(static key =>
                key.Contains("EditSession", StringComparison.Ordinal)
                || key.Contains("CloneHelpers", StringComparison.Ordinal)
                || key.Contains("ReadOnlyAdapters", StringComparison.Ordinal)
            )
        )
        {
            afterSources[hint].ShouldBe(beforeSources[hint]);
        }

        foreach (
            var stage in new[]
            {
                "SparseFragmentsGenerator.GeneratedOncePlan",
                "SparseFragmentsGenerator.GeneratedOnceEditSession",
                "SparseFragmentsGenerator.GeneratedOnceCloneHelpers",
                "SparseFragmentsGenerator.GeneratedOnceReadOnlyAdapters",
            }
        )
        {
            // Value-equal recomputation (Unchanged) is stable output just
            // like Cached: no helper bytes churn on unrelated edits.
            NoneModified(after, stage);
        }
    }

    [Test]
    public void CapabilityEdit_UpdatesOnlyAffectedFamilies()
    {
        var before = Run(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Scalar.cs"] = ScalarModel },
            tracked: true
        );
        before.SpfDiagnostics().ShouldBeEmpty();
        before
            .Sources()
            .Keys.ShouldNotContain(key => key.Contains("CloneHelpers", StringComparison.Ordinal));
        var editSessionBefore = before
            .Sources()
            .First(pair => pair.Key.EndsWith("EditSessionCore.g.cs", StringComparison.Ordinal))
            .Value;
        var after = Rerun(
            before,
            "Scalar.cs",
            CollectionModel.Replace("CollectionModel", "ScalarModel")
        );
        after.SpfDiagnostics().ShouldBeEmpty();
        var afterSources = after.Sources();
        afterSources
            .Keys.Any(key => key.Contains("CloneHelpers", StringComparison.Ordinal))
            .ShouldBeTrue();
        afterSources
            .Keys.Any(key => key.Contains("ReadOnlyAdapters", StringComparison.Ordinal))
            .ShouldBeTrue();
        afterSources
            .First(pair => pair.Key.EndsWith("EditSessionCore.g.cs", StringComparison.Ordinal))
            .Value.ShouldBe(editSessionBefore);
        // The unaffected family recomputes value-equal; the newly
        // requested families change output.
        NoneModified(after, "SparseFragmentsGenerator.GeneratedOnceEditSession");
        HasModified(after, "SparseFragmentsGenerator.GeneratedOnceCloneHelpers");
        HasModified(after, "SparseFragmentsGenerator.GeneratedOnceReadOnlyAdapters");
    }

    [Test]
    public void GeneratedSourcesParseUnderCSharp9()
    {
        var probe = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Scalar.cs"] = ScalarModel,
                ["Collection.cs"] = CollectionModel,
                ["Keyed.cs"] = KeyedModel,
            }
        );
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        foreach (var source in probe.Sources().Values)
        {
            CSharpSyntaxTree
                .ParseText(source, parseOptions)
                .GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty();
        }
    }

    [Test]
    public void ImplementationsStayInternalBehindPublicContracts()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Order.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                namespace ConsumerNs
                {
                    [SparseFragmentModel]
                    public partial class Order
                    {
                        public string Label { get; set; } = "";
                        public List<string> Tags { get; set; } = new();
                    }
                }
                """,
        };
        var compilation = CreateCompilation(files);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        updated
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        using var stream = new MemoryStream();
        updated.Emit(stream).Success.ShouldBeTrue();
        var consumer = System.Reflection.Assembly.Load(stream.ToArray());
        // Public contracts stay public and usable.
        consumer.GetType("ConsumerNs.Order").ShouldNotBeNull();
        consumer.GetType("ConsumerNs.Order+Fragment").ShouldNotBeNull();
        consumer.GetType("ConsumerNs.Order+Patch").ShouldNotBeNull();
        consumer.GetType("ConsumerNs.Order+ChangeSet").ShouldNotBeNull();
        // Generated-Once implementations never escape through public API.
        consumer.GetType("SparseFragments.Generated.SparseCloneHelpers").ShouldNotBeNull();
        consumer
            .GetType("SparseFragments.Generated.SparseCloneHelpers")!
            .IsNotPublic.ShouldBeTrue();
        consumer
            .GetExportedTypes()
            .Where(static type =>
                type.Name.Contains("SparseClone", StringComparison.Ordinal)
                || type.Name.Contains("Adapter", StringComparison.Ordinal)
                || type.Name.Contains("RemovalIndex", StringComparison.Ordinal)
            )
            .ShouldBeEmpty();

        // A separate assembly consumes only the public contracts and executes behavior.
        var check = CSharpSyntaxTree.ParseText(
            """
            using System.Collections.Generic;
            using System.Linq;
            public static class ConsumerCheck
            {
                public static string Run()
                {
                    var before = new ConsumerNs.Order { Label = "a" };
                    var after = new ConsumerNs.Order { Label = "b", Tags = new List<string> { "x" } };
                    var changes = ConsumerNs.Order.ChangeSet.Between(before, after);
                    if (!changes.TryApplyTo(before, out var updated, out var conflicts)) return "apply-failed";
                    var patch = changes.ToPatch();
                    var current = new ConsumerNs.Order { Label = "a" };
                    var applied = patch.ApplyTo(current);
                    return updated.Label + ":" + string.Join(",", updated.Tags) + ":" + applied.Label;
                }
            }
            """,
            path: "ConsumerCheck.cs"
        );
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var checkReferences = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        checkReferences.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        checkReferences.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        var checkCompilation = CSharpCompilation.Create(
            "ConsumerCheck",
            [check],
            checkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        checkCompilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        using var checkStream = new MemoryStream();
        checkCompilation.Emit(checkStream).Success.ShouldBeTrue();
        var checkAssembly = System.Reflection.Assembly.Load(checkStream.ToArray());
        // Assemblies loaded from bytes resolve by name through this
        // handler so the check assembly finds its consumer compilation.
        var resolver = new System.ResolveEventHandler(
            (sender, args) =>
            {
                var name = new System.Reflection.AssemblyName(args.Name);
                if (string.Equals(name.Name, consumer.GetName().Name, StringComparison.Ordinal))
                {
                    return consumer;
                }

                return null;
            }
        );
        System.AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            checkAssembly
                .GetType("ConsumerCheck")!
                .GetMethod("Run")!
                .Invoke(null, null)
                .ShouldBe("b:x:b");
        }
        finally
        {
            System.AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    private static Compilation RunAndUpdate(CSharpCompilation compilation, GeneratorDriver driver)
    {
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return updated;
    }
}
