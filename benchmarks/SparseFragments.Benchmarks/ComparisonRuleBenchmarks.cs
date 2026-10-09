using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

public enum ComparisonRuleShape
{
    None,
    Agreement,
    Missing,
    Conflicting,
    Assembly,
    Local,
    Unrelated,
    LocalWithInheritedType,
}

[MemoryDiagnoser]
public class ComparisonRuleBenchmarks
{
    [Params(16, 256)]
    public int RootCount { get; set; }

    [Params(0, 128)]
    public int UnrelatedTypeCount { get; set; }

    [Params(0, 2)]
    public int NestedDepth { get; set; }

    [ParamsAllValues]
    public ComparisonRuleShape Shape { get; set; }

    private INamedTypeSymbol _model = null!;
    private ImmutableArray<INamedTypeSymbol> _roots = ImmutableArray<INamedTypeSymbol>.Empty;
    private ITypeSymbol _valueType = null!;
    private SparseGeneratorConfig _config = null!;

    [GlobalSetup]
    public void Setup()
    {
        _config = (SparseGeneratorConfig)(
            typeof(SparseFragmentsGenerator)
                .GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null)
            ?? throw new InvalidOperationException("Missing generator configuration.")
        );
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "ComparisonRuleProbe",
            new[] { CSharpSyntaxTree.ParseText(CreateSource()) },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        if (errors.Any())
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
        _model = compilation.GetTypeByMetadataName("RuleTarget")!;
        _roots = compilation
            .Assembly.GlobalNamespace.GetMembers()
            .OfType<INamedTypeSymbol>()
            .Where(static type => type.Name.StartsWith("RuleRoot", StringComparison.Ordinal))
            .ToImmutableArray();
        _valueType = compilation.GetSpecialType(SpecialType.System_String);
        var expected = Shape switch
        {
            ComparisonRuleShape.Agreement => "RuleComparerA",
            ComparisonRuleShape.Assembly
            or ComparisonRuleShape.Local
            or ComparisonRuleShape.LocalWithInheritedType => "RuleComparerB",
            _ => null,
        };
        var rules = SparseComparisonRules.CreateRuleSet(_model, _config, CancellationToken.None);
        var found = rules.TryGetComparerType(_valueType, out var comparer);
        if (found != (expected is not null) || comparer?.Name != expected)
        {
            throw new InvalidOperationException(
                "Comparison rule precedence or inheritance changed."
            );
        }
        if (Analyze() != found)
        {
            throw new InvalidOperationException("Repeated comparison analysis changed its result.");
        }
        if (
            Shape == ComparisonRuleShape.LocalWithInheritedType
            && (
                !rules.TryGetComparerType(
                    compilation.GetSpecialType(SpecialType.System_Int32),
                    out var inherited
                )
                || inherited?.Name != "RuleIntComparer"
            )
        )
        {
            throw new InvalidOperationException(
                "A local rule must not hide inherited rules for other types."
            );
        }
    }

    private string CreateSource()
    {
        var source = new StringBuilder(
            "using System.Collections.Generic; using SparseFragments;\n"
        );
        if (Shape == ComparisonRuleShape.Assembly)
        {
            source.AppendLine("[assembly: SparseCompare(typeof(string), typeof(RuleComparerB))]");
        }
        if (Shape is ComparisonRuleShape.Local or ComparisonRuleShape.LocalWithInheritedType)
        {
            source.AppendLine("[SparseCompare(typeof(string), typeof(RuleComparerB))]");
        }
        source.AppendLine(
            "public class RuleTarget { public string Value { get; set; } = string.Empty; }"
        );
        for (var depth = NestedDepth; depth >= 0; depth--)
        {
            var child = depth == NestedDepth ? "RuleTarget" : "RuleChain" + (depth + 1);
            source
                .Append("public class RuleChain")
                .Append(depth)
                .Append(" { public ")
                .Append(child)
                .AppendLine(" Child { get; set; } = new(); }");
        }
        source.AppendLine(
            "public class RuleComparerA : IEqualityComparer<string> { public bool Equals(string? a, string? b) => a == b; public int GetHashCode(string value) => value.GetHashCode(); }"
        );
        source.AppendLine("public class RuleComparerB : RuleComparerA { }");
        if (Shape == ComparisonRuleShape.LocalWithInheritedType)
        {
            source.AppendLine(
                "public class RuleIntComparer : IEqualityComparer<int> { public bool Equals(int a, int b) => a == b; public int GetHashCode(int value) => value; }"
            );
        }
        for (var index = 0; index < RootCount; index++)
        {
            var comparer = Shape switch
            {
                ComparisonRuleShape.Agreement
                or ComparisonRuleShape.Local
                or ComparisonRuleShape.LocalWithInheritedType => "RuleComparerA",
                ComparisonRuleShape.Missing when index != 0 => "RuleComparerA",
                ComparisonRuleShape.Conflicting => index == 0 ? "RuleComparerB" : "RuleComparerA",
                _ => null,
            };
            if (comparer is not null)
            {
                source
                    .Append("[SparseCompare(typeof(string), typeof(")
                    .Append(comparer)
                    .AppendLine("))]");
            }
            if (Shape == ComparisonRuleShape.LocalWithInheritedType)
            {
                source.AppendLine("[SparseCompare(typeof(int), typeof(RuleIntComparer))]");
            }
            source
                .Append("[SparseFragmentModel] public partial class RuleRoot")
                .Append(index)
                .AppendLine(" { public RuleChain0 Child { get; set; } = new(); }");
        }
        for (var index = 0; index < UnrelatedTypeCount; index++)
        {
            // Unrelated consumer types: no model or comparison attributes and
            // no reference to the target, but they still enter assembly scans.
            source
                .Append("public class UnrelatedHolder")
                .Append(index)
                .AppendLine(
                    " { public string Name { get; set; } = string.Empty; public List<int> Scores { get; set; } = new(); }"
                );
        }
        if (Shape == ComparisonRuleShape.Unrelated)
        {
            source.AppendLine(
                "[SparseFragmentModel, SparseCompare(typeof(string), typeof(RuleComparerA))] public partial class UnrelatedRoot { public string Value { get; set; } = string.Empty; }"
            );
        }
        return source.ToString();
    }

    [Benchmark]
    public bool Analyze() =>
        SparseComparisonRules
            .CreateRuleSet(_model, _config, CancellationToken.None)
            .TryGetComparerType(_valueType, out _);

    // Amortized cost across every fragment root in the compilation. The first
    // model builds the per-compilation reference index; the rest reuse it, so
    // this stays near-linear while per-model assembly scans grow
    // quadratically. Incremental invalidation is pinned separately by
    // GeneratorStepTrackingTests (unrelated edits keep Analysis cached).
    [Benchmark]
    public int AnalyzeAllModels()
    {
        var hits = 0;
        foreach (var root in _roots)
        {
            if (
                SparseComparisonRules
                    .CreateRuleSet(root, _config, CancellationToken.None)
                    .TryGetComparerType(_valueType, out _)
            )
            {
                hits++;
            }
        }

        return hits;
    }
}
