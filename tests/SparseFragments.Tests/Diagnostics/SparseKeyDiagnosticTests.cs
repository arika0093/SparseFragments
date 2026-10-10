using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Diagnostics;

/// <summary>Generator diagnostic tests for the single-property SparseKey API.</summary>
/// <remarks>
/// Identity is declared with exactly one property marked with parameterless
/// <c>[SparseKey]</c>. Tuple and value-object keys use a computed key property
/// whose declared type is the key type.
/// </remarks>
public sealed class SparseKeyDiagnosticTests
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
            "SparseKeyDiagnosticsProbe",
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

    private static void AssertSpfIds(
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
        public partial class KeyHolder
        {
            public List<Keyed> Items { get; set; } = new();
        }
        """;

    private static string WithHolder(string element) =>
        "using SparseFragments;\nusing System;\nusing System.Collections.Generic;\n"
        + element
        + "\n"
        + HolderTail;

    [Test]
    public void Spf013_MultiplePropertyKeysReportError()
    {
        const string element = """
            public partial class Keyed
            {
                [SparseKey]
                public string A { get; set; } = "";
                [SparseKey]
                public string B { get; set; } = "";
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF013"]);
        diagnostics.Single(d => d.Id == "SPF013").GetMessage().ShouldContain("Keyed");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf014_TypeLevelAttributeReportsError()
    {
        // The attribute only targets properties, so the compilation also errors;
        // the generator reports the declaration shape specifically.
        const string element = """
            [SparseKey]
            public partial class Keyed
            {
                public string Id { get; set; } = "";
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF014"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf024_IncompatibleUnassignedSentinelReportsError()
    {
        const string element = """
            public partial class Keyed
            {
                [SparseKey(Unassigned = "none")]
                public int Id { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF024"]);
        diagnostics.Single(d => d.Id == "SPF024").GetMessage().ShouldContain("not convertible");
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("staticProperty")]
    [Arguments("indexer")]
    [Arguments("privateGetter")]
    public void Spf017_InaccessibleKeyReportsError(string kind)
    {
        // Each element keeps an ordinary instance property so it stays a
        // structural candidate; only the key declaration itself is invalid.
        var element = kind switch
        {
            "staticProperty" => """
                public partial class Keyed
                {
                    [SparseKey]
                    public static string Id { get; set; } = "";
                    public string Name { get; set; } = "";
                }
                """,
            "indexer" => """
                public partial class Keyed
                {
                    [SparseKey]
                    public string this[int index] => "";
                    public string Id { get; set; } = "";
                }
                """,
            "privateGetter" => """
                public partial class Keyed
                {
                    [SparseKey]
                    public string Id { private get; set; } = "";
                    public string Name { get; set; } = "";
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF017"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("nullableProperty")]
    [Arguments("nullableValueProperty")]
    [Arguments("explicitNull")]
    [Arguments("override")]
    [Arguments("nullableTupleProperty")]
    public void NullableScalarKeysGenerateWithoutDiagnostics(string kind)
    {
        var element = kind switch
        {
            "nullableProperty" => """
                public partial class Keyed
                {
                    [SparseKey]
                    public string? Id { get; set; }
                }
                """,
            "nullableValueProperty" => """
                public partial class Keyed
                {
                    [SparseKey]
                    public int? Id { get; set; }
                }
                """,
            "explicitNull" => """
                public partial class Keyed
                {
                    [SparseKey(Unassigned = null)]
                    public string? Id { get; set; }
                }
                """,
            "override" => """
                public partial class Keyed
                {
                    [SparseKey(Unassigned = "pending")]
                    public string? Id { get; set; }
                }
                """,
            // Computed nullable tuple keys may use null to denote unassigned.
            "nullableTupleProperty" => """
                public partial class Keyed
                {
                    public string TenantId { get; set; } = "";
                    public string? Id { get; set; }
                    [SparseKey]
                    public (string TenantId, string? Id)? Key => Id is null ? null : (TenantId, Id);
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources.Any(s => s.HintName.Contains("KeyHolder", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Test]
    [Arguments("listProperty")]
    [Arguments("nullableListProperty")]
    public void Spf019_CollectionShapedKeyReportsError(string kind)
    {
        var element = kind switch
        {
            "listProperty" => """
                public partial class Keyed
                {
                    [SparseKey]
                    public List<string> Ids { get; set; } = new();
                }
                """,
            "nullableListProperty" => """
                public partial class Keyed
                {
                    [SparseKey]
                    public List<string>? Ids { get; set; }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF019"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    public void NestedInvalidKeyFailsRoot()
    {
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            public partial class NestedMid
            {
                [SparseKey]
                public string Name { get; set; } = "";
                public List<NestedLeaf> Items { get; set; } = new();
            }
            public partial class NestedLeaf
            {
                [SparseKey]
                public List<string> Ids { get; set; } = new();
            }
            [SparseFragmentModel]
            public partial class NestedRoot
            {
                public List<NestedMid> Groups { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        AssertSpfIds(diagnostics, ["SPF019"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("computedProperty")]
    [Arguments("tupleProperty")]
    [Arguments("normalizedProperty")]
    public void ValidKeyShapesGenerateWithoutDiagnostics(string kind)
    {
        var element = kind switch
        {
            "computedProperty" => """
                public readonly record struct ComputedKey(string Tenant, int Id);
                public partial class Keyed
                {
                    public string Tenant { get; set; } = "";
                    public int Id { get; set; }
                    [SparseKey]
                    public ComputedKey Key => new(Tenant, Id);
                }
                """,
            "tupleProperty" => """
                public partial class Keyed
                {
                    public string TenantId { get; set; } = "";
                    public int Id { get; set; }
                    [SparseKey]
                    public (string TenantId, int Id) Key => (TenantId, Id);
                }
                """,
            "normalizedProperty" => """
                public readonly record struct NormalKey(string Tenant, int Id);
                public partial class Keyed
                {
                    public string Tenant { get; set; } = "";
                    public int Id { get; set; }
                    [SparseKey]
                    public NormalKey Key => new(Tenant.ToUpperInvariant(), Id);
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources.Any(s => s.HintName.Contains("KeyHolder", StringComparison.Ordinal)).ShouldBeTrue();
    }
}
