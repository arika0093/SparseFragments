using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Diagnostics;

/// <summary>Generator diagnostic tests for the temporary-identity declaration.</summary>
/// <remarks>
/// A keyed element type may declare exactly one parameterless
/// <c>[SparseTemporaryKey]</c> property of type <c>Guid?</c>, and its key must
/// have unassigned semantics. Explicit initializers warn (SPF032) but still
/// generate.
/// </remarks>
public sealed class SparseTemporaryKeyDiagnosticTests
{
    private static (
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<GeneratedSourceResult> Sources
    ) Run(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
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
        var compilation = CSharpCompilation.Create(
            "SparseTemporaryKeyDiagnosticsProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (
            runResult.Diagnostics,
            runResult.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray()
        );
    }

    private static void AssertSpfErrorIds(
        ImmutableArray<Diagnostic> diagnostics,
        params string[] expectedIds
    )
    {
        var actual = diagnostics
            .Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .Select(static d => d.Id)
            .OrderBy(static id => id)
            .ToArray();
        actual.ShouldBe(expectedIds.OrderBy(static id => id).ToArray());
        foreach (
            var diagnostic in diagnostics.Where(d =>
                d.Id.StartsWith("SPF", StringComparison.Ordinal)
            )
        )
        {
            diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
            diagnostic.Descriptor.HelpLinkUri.ShouldStartWith(
                "https://github.com/arika0093/SparseFragments/blob/main/docs/analyzer.md#spf"
            );
        }
    }

    private const string HolderTail = """
        [SparseFragmentModel]
        public partial class TempKeyHolder
        {
            public List<TempKeyed> Items { get; set; } = new();
        }
        """;

    private const string ValidElement = """
        public partial class TempKeyed
        {
            [SparseKey(Unassigned = 0)]
            public int Id { get; set; }

            [SparseTemporaryKey]
            public Guid? TemporaryId { get; set; }

            public string Name { get; set; } = "";
        }
        """;

    private static string WithHolder(string element) =>
        "using SparseFragments;\nusing System;\nusing System.Collections.Generic;\n"
        + element
        + "\n"
        + HolderTail;

    [Test]
    public void ValidDeclarationProducesNoDiagnostics()
    {
        var (diagnostics, sources) = Run(WithHolder(ValidElement));
        diagnostics
            .Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        sources.ShouldNotBeEmpty();
    }

    [Test]
    [Arguments("Guid")]
    [Arguments("int")]
    [Arguments("string")]
    [Arguments("withArgs")]
    public void Spf031_UnsupportedTemporaryTypeReportsError(string kind)
    {
        var declaration = kind switch
        {
            "Guid" => "[SparseTemporaryKey]\npublic Guid TemporaryId { get; set; }",
            "int" => "[SparseTemporaryKey]\npublic int? TemporaryId { get; set; }",
            "string" => "[SparseTemporaryKey]\npublic string? TemporaryId { get; set; }",
            _ => "[SparseTemporaryKey(true)]\npublic Guid? TemporaryId { get; set; }",
        };
        var element =
            """
            public partial class TempKeyed
            {
                [SparseKey(Unassigned = 0)]
                public int Id { get; set; }

            """
            + "    "
            + declaration.Replace("\n", "\n    ")
            + "\n}";
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfErrorIds(diagnostics, ["SPF031"]);
        diagnostics.Single(d => d.Id == "SPF031").GetMessage().ShouldContain("TempKeyed");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf031_MultipleMarksReportError()
    {
        const string element = """
            public partial class TempKeyed
            {
                [SparseKey(Unassigned = 0)]
                public int Id { get; set; }

                [SparseTemporaryKey]
                public Guid? First { get; set; }

                [SparseTemporaryKey]
                public Guid? Second { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfErrorIds(diagnostics, ["SPF031"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("privateSetter")]
    [Arguments("getterOnly")]
    [Arguments("staticProperty")]
    public void Spf031_InaccessibleTemporaryPropertyReportsError(string kind)
    {
        var declaration = kind switch
        {
            "privateSetter" => "[SparseTemporaryKey]\npublic Guid? TemporaryId { get; private set; }",
            "getterOnly" => "[SparseTemporaryKey]\npublic Guid? TemporaryId { get; }",
            _ => "[SparseTemporaryKey]\npublic static Guid? TemporaryId { get; set; }",
        };
        var element =
            """
            public partial class TempKeyed
            {
                [SparseKey(Unassigned = 0)]
                public int Id { get; set; }

            """
            + "    "
            + declaration.Replace("\n", "\n    ")
            + "\n}";
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfErrorIds(diagnostics, ["SPF031"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf031_KeyWithoutUnassignedSemanticsReportsError()
    {
        const string element = """
            public partial class TempKeyed
            {
                [SparseKey]
                public int Id { get; set; }

                [SparseTemporaryKey]
                public Guid? TemporaryId { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfErrorIds(diagnostics, ["SPF031"]);
        diagnostics
            .Single(d => d.Id == "SPF031")
            .GetMessage()
            .ShouldContain("unassigned");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf031_MissingKeyReportsError()
    {
        const string element = """
            public partial class TempKeyed
            {
                public int Id { get; set; }

                [SparseTemporaryKey]
                public Guid? TemporaryId { get; set; }
            }
            """;
        var (diagnostics, _) = Run(WithHolder(element));
        diagnostics.Select(static d => d.Id).ShouldContain("SPF031");
    }

    [Test]
    public void Spf031_CombinedKeyAttributesReportError()
    {
        const string element = """
            public partial class TempKeyed
            {
                [SparseKey(Unassigned = 0)]
                [SparseTemporaryKey]
                public int Id { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfErrorIds(diagnostics, ["SPF031"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf031_IgnoredPropertyReportsError()
    {
        const string element = """
            public partial class TempKeyed
            {
                [SparseKey(Unassigned = 0)]
                public int Id { get; set; }

                [SparseIgnore]
                [SparseTemporaryKey]
                public Guid? TemporaryId { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfErrorIds(diagnostics, ["SPF031"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("null")]
    [Arguments("default")]
    [Arguments("newGuid")]
    [Arguments("explicitValue")]
    public void Spf032_ExplicitInitializerWarnsButGenerates(string kind)
    {
        var initializer = kind switch
        {
            "null" => " = null",
            "default" => " = default",
            "newGuid" => " = Guid.NewGuid()",
            _ => " = new Guid(\"11111111-1111-1111-1111-111111111111\")",
        };
        var element =
            """
            public partial class TempKeyed
            {
                [SparseKey(Unassigned = 0)]
                public int Id { get; set; }

                [SparseTemporaryKey]
                public Guid? TemporaryId { get; set; }
            """
            + initializer
            + ";}";
        var (diagnostics, sources) = Run(WithHolder(element));
        var warning = diagnostics.Single(static d => d.Id == "SPF032");
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.GetMessage().ShouldContain("TempKeyed.TemporaryId");
        warning.Descriptor.HelpLinkUri.ShouldBe(
            "https://github.com/arika0093/SparseFragments/blob/main/docs/analyzer.md#spf032-temporary-key-property-initializer"
        );
        diagnostics
            .Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal) && d.Id != "SPF032")
            .ShouldBeEmpty();
        sources.ShouldNotBeEmpty();
    }
}
