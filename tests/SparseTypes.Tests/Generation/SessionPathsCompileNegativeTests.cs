using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

// Compile-time proofs for issue #270: wrong root, value, and key types fail
// on session-owned navigation.
public sealed class SessionPathsCompileNegativeTests
{
    private const string ProbeSource = """
        using SparseTypes;
        using System.Collections.Generic;
        namespace PathsNegativeProbe
        {
            [SparseModel]
            public partial class NegCustomer
            {
                public string Name { get; set; } = "";
            }
            [SparseModel]
            public partial class NegItem
            {
                [SparseKey]
                public string Id { get; set; } = "";
                public string Title { get; set; } = "";
            }
            [SparseModel]
            public partial class NegOrder
            {
                public NegCustomer Customer { get; set; } = new();
                public List<NegItem> Items { get; set; } = new();
                public Dictionary<string, int> Scores { get; set; } = new();
            }
        }
        """;

    private static Compilation RunProbe()
    {
        var tree = CSharpSyntaxTree.ParseText(ProbeSource, path: "Models.cs");
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "PathsNegativeProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        updated
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        return updated;
    }

    private static string NegativeDiagnostics(string snippet)
    {
        var probe = RunProbe();
        var tree = CSharpSyntaxTree.ParseText(snippet, path: "Negative.cs");
        var updated = probe.AddSyntaxTrees(tree);
        var diagnostics = updated
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.Id + ": " + d.GetMessage())
            .ToArray();
        return string.Join("\n", diagnostics);
    }

    [Test]
    public void SessionNavigationCompilesWithCorrectTypes()
    {
        var probe = RunProbe();
        const string snippet = """
            using SparseTypes;
            namespace PathsNegativeProbe
            {
                file static class PositiveControl
                {
                    public static void Check()
                    {
                        var session = new NegOrder().CreateEditSession();
                        SparseTypes.SparsePath<NegOrder, string> name = session.Paths.Customer.Name;
                        SparseTypes.SparsePath<NegOrder, string> title = session.Paths.Items.Key("a").Title;
                        SparseTypes.SparsePath<NegOrder, int> score = session.Paths.Scores.Key("k");
                        var state = session.FieldState(session.Paths.Customer.Name);
                    }
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(snippet, path: "Positive.cs");
        probe
            .AddSyntaxTrees(tree)
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    [Test]
    public void WrongValueTypeIsRejected()
    {
        NegativeDiagnostics(
                """
                using SparseTypes;
                namespace PathsNegativeProbe
                {
                    file static class WrongValue
                    {
                        public static void Check()
                        {
                            var session = new NegOrder().CreateEditSession();
                            SparseTypes.SparsePath<NegOrder, int> bad = session.Paths.Customer.Name;
                        }
                    }
                }
                """
            )
            .ShouldContain("CS0029");
    }

    [Test]
    public void WrongRootTypeIsRejected()
    {
        NegativeDiagnostics(
                """
                using SparseTypes;
                namespace PathsNegativeProbe
                {
                    file static class WrongRoot
                    {
                        public static void Check()
                        {
                            var session = new NegOrder().CreateEditSession();
                            SparseTypes.SparsePath<NegCustomer, string> bad = session.Paths.Customer.Name;
                        }
                    }
                }
                """
            )
            .ShouldContain("CS0029");
    }

    [Test]
    public void WrongKeyTypeIsRejected()
    {
        NegativeDiagnostics(
                """
                using SparseTypes;
                namespace PathsNegativeProbe
                {
                    file static class WrongKey
                    {
                        public static void Check()
                        {
                            var session = new NegOrder().CreateEditSession();
                            var bad = session.Paths.Items.Key(123).Title;
                        }
                    }
                }
                """
            )
            .ShouldContain("CS1503");
    }

    [Test]
    public void CollectionBuilderDoesNotPoseAsScalarLeaf()
    {
        NegativeDiagnostics(
                """
                using SparseTypes;
                namespace PathsNegativeProbe
                {
                    file static class CollectionAsLeaf
                    {
                        public static void Check()
                        {
                            var session = new NegOrder().CreateEditSession();
                            SparseTypes.SparsePath<NegOrder, int> bad = session.Paths.Items;
                        }
                    }
                }
                """
            )
            .ShouldContain("CS0029");
    }
}
