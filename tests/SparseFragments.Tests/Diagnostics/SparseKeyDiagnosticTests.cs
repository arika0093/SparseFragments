using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Diagnostics;

/// <summary>Generator diagnostic tests for the SparseKey API (SPF012–SPF020).</summary>
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
    [Arguments("propertyAndType")]
    [Arguments("propertyAndInterface")]
    [Arguments("typeAndInterface")]
    public void Spf012_ConflictingMechanismsReportError(string kind)
    {
        var element = kind switch
        {
            "propertyAndType" => """
                [SparseKey(nameof(TenantId), nameof(Id))]
                public partial class Keyed
                {
                    [SparseKey]
                    public string Id { get; set; } = "";
                    public string TenantId { get; set; } = "";
                }
                """,
            "propertyAndInterface" => """
                public partial class Keyed : ISparseKeyed<string>
                {
                    [SparseKey]
                    public string Id { get; set; } = "";
                    public string SparseKey => Id;
                }
                """,
            "typeAndInterface" => """
                [SparseKey(nameof(TenantId), nameof(Id))]
                public partial class Keyed : ISparseKeyed<string>
                {
                    public string TenantId { get; set; } = "";
                    public string Id { get; set; } = "";
                    public string SparseKey => TenantId + Id;
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF012"]);
        diagnostics.Single(d => d.Id == "SPF012").GetMessage().ShouldContain("Keyed");
        sources.ShouldBeEmpty();
    }

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
    [Arguments("parameterlessOnType")]
    [Arguments("argsOnProperty")]
    public void Spf014_InvalidShapesReportError(string kind)
    {
        var element = kind switch
        {
            "parameterlessOnType" => """
                [SparseKey]
                public partial class Keyed
                {
                    public string Id { get; set; } = "";
                }
                """,
            "argsOnProperty" => """
                public partial class Keyed
                {
                    [SparseKey("Id")]
                    public string Id { get; set; } = "";
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        // Parameterless-on-type also leaves the sequence unkeyed, but the specific
        // shape error takes precedence over SPF011.
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
    public void Spf025_CompositeSentinelIsRejected()
    {
        const string element = """
            [SparseKey(nameof(Tenant), nameof(Id), Unassigned = 0)]
            public partial class Keyed
            {
                public int Tenant { get; set; }
                public int Id { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF025"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf015_MissingComponentReportsError()
    {
        const string element = """
            [SparseKey("TenantId", "Nope")]
            public partial class Keyed
            {
                public string TenantId { get; set; } = "";
                public string Id { get; set; } = "";
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF015"]);
        diagnostics.Single(d => d.Id == "SPF015").GetMessage().ShouldContain("Keyed.Nope");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf016_DuplicateComponentReportsError()
    {
        const string element = """
            [SparseKey("Id", "Id")]
            public partial class Keyed
            {
                public string Id { get; set; } = "";
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF016"]);
        diagnostics.Single(d => d.Id == "SPF016").GetMessage().ShouldContain("Keyed.Id");
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("staticProperty")]
    [Arguments("indexer")]
    [Arguments("privateGetter")]
    [Arguments("staticComponent")]
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
            "staticComponent" => """
                [SparseKey("Id")]
                public partial class Keyed
                {
                    public static string Id { get; set; } = "";
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
    public void Spf018_NullableCompositeComponentReportsError()
    {
        const string element = """
            [SparseKey("TenantId", "Id")]
            public partial class Keyed
            {
                public string TenantId { get; set; } = "";
                public string? Id { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF018"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("nullableProperty")]
    [Arguments("nullableValueProperty")]
    [Arguments("nullableInterfaceKey")]
    [Arguments("explicitNull")]
    [Arguments("override")]
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
            "nullableInterfaceKey" => """
                public partial class Keyed : ISparseKeyed<string?>
                {
                    public string? Id { get; set; }
                    public string? SparseKey => Id;
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
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources.Any(s => s.HintName.Contains("KeyHolder", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Test]
    [Arguments("listProperty")]
    [Arguments("nullableListProperty")]
    [Arguments("arrayComponent")]
    [Arguments("dictionaryInterfaceKey")]
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
            "arrayComponent" => """
                [SparseKey("Tags", "Id")]
                public partial class Keyed
                {
                    public string[] Tags { get; set; } = [];
                    public string Id { get; set; } = "";
                }
                """,
            "dictionaryInterfaceKey" => """
                public partial class Keyed : ISparseKeyed<Dictionary<string, int>>
                {
                    public string Id { get; set; } = "";
                    public Dictionary<string, int> SparseKey => new();
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF019"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("explicitImplementation")]
    [Arguments("missingAccessor")]
    public void Spf020_InvalidInterfaceReportsError(string kind)
    {
        var element = kind switch
        {
            "explicitImplementation" => """
                public partial class Keyed : ISparseKeyed<string>
                {
                    public string Id { get; set; } = "";
                    string ISparseKeyed<string>.SparseKey => Id;
                }
                """,
            // Declared but unimplemented: the compilation itself errors (CS0535), and
            // the generator additionally reports the unusable key contract.
            "missingAccessor" => """
                public partial class Keyed : ISparseKeyed<string>
                {
                    public string Id { get; set; } = "";
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        AssertSpfIds(diagnostics, ["SPF020"]);
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
            [SparseKey(nameof(Tenant), nameof(Id))]
            public partial class NestedLeaf
            {
                public string Tenant { get; set; } = "";
                public string? Id { get; set; }
            }
            [SparseFragmentModel]
            public partial class NestedRoot
            {
                public List<NestedMid> Groups { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        AssertSpfIds(diagnostics, ["SPF018"]);
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("computedProperty")]
    [Arguments("twoComponent")]
    [Arguments("threeComponent")]
    [Arguments("interface")]
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
            "twoComponent" => """
                [SparseKey(nameof(TenantId), nameof(Id))]
                public partial class Keyed
                {
                    public string TenantId { get; set; } = "";
                    public int Id { get; set; }
                }
                """,
            "threeComponent" => """
                [SparseKey(nameof(A), nameof(B), nameof(C))]
                public partial class Keyed
                {
                    public int A { get; set; }
                    public string B { get; set; } = "";
                    public Guid C { get; set; }
                }
                """,
            "interface" => """
                public readonly record struct IfaceKey(string Tenant, int Id);
                public partial class Keyed : ISparseKeyed<IfaceKey>
                {
                    public string Tenant { get; set; } = "";
                    public int Id { get; set; }
                    public IfaceKey SparseKey => new(Tenant.ToUpperInvariant(), Id);
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(WithHolder(element));
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources.Any(s => s.HintName.Contains("KeyHolder", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Test]
    public void CompositeKeyMemberNamesDoNotCollide()
    {
        // The tuple representation introduces no generated identifiers, so user
        // members named like tuple machinery must not break generation.
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            [SparseKey(nameof(TenantId), nameof(Id))]
            public partial class Keyed
            {
                public string TenantId { get; set; } = "";
                public int Id { get; set; }
                public string Key { get; set; } = "";
                public string Item1 { get; set; } = "";
                public string Rest { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class KeyHolder
            {
                public List<Keyed> Items { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources.Any(s => s.HintName.Contains("KeyHolder", StringComparison.Ordinal)).ShouldBeTrue();
    }
}
