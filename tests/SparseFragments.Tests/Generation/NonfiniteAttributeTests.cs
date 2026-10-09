using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

[AttributeUsage(AttributeTargets.Property)]
public sealed class NonfiniteProbeAttribute : Attribute
{
    public NonfiniteProbeAttribute(double value) => Value = value;

    public double Value { get; }

    public double NamedDouble { get; set; }

    public float NamedSingle { get; set; }
}

[SparseFragmentModel]
public partial class NonfiniteAttributeModel
{
    [NonfiniteProbe(double.NaN, NamedDouble = double.PositiveInfinity, NamedSingle = float.NegativeInfinity)]
    public string First { get; set; } = string.Empty;

    [NonfiniteProbe(double.NegativeInfinity, NamedDouble = 1.5, NamedSingle = 2.5f)]
    public string Second { get; set; } = string.Empty;

    [NonfiniteProbe(3.25, NamedDouble = double.NaN, NamedSingle = float.NaN)]
    public string Third { get; set; } = string.Empty;
}

public sealed class NonfiniteAttributeTests
{
    private const string ProbeSource = """
        using SparseFragments;
        using SparseFragments.Tests.Generation;
        namespace NonfiniteProbe
        {
            [SparseFragmentModel]
            public partial class ProbeModel
            {
                [NonfiniteProbe(double.NaN, NamedDouble = double.PositiveInfinity, NamedSingle = float.NegativeInfinity)]
                public string First { get; set; } = string.Empty;
                [NonfiniteProbe(1.5, NamedDouble = 2.5, NamedSingle = 0.5f)]
                public string Second { get; set; } = string.Empty;
            }
        }
        """;

    [Test]
    public void DescriptorsRetainNonfiniteAttributeValues()
    {
        var session = new NonfiniteAttributeModel().CreateEditSession();
        session.Descriptors.TryGet(nameof(NonfiniteAttributeModel.First), out var first).ShouldBeTrue();
        var firstAttribute = first.Attributes.OfType<NonfiniteProbeAttribute>().Single();
        double.IsNaN(firstAttribute.Value).ShouldBeTrue();
        double.IsPositiveInfinity(firstAttribute.NamedDouble).ShouldBeTrue();
        float.IsNegativeInfinity(firstAttribute.NamedSingle).ShouldBeTrue();

        session.Descriptors.TryGet(nameof(NonfiniteAttributeModel.Second), out var second).ShouldBeTrue();
        var secondAttribute = second.Attributes.OfType<NonfiniteProbeAttribute>().Single();
        double.IsNegativeInfinity(secondAttribute.Value).ShouldBeTrue();
        secondAttribute.NamedDouble.ShouldBe(1.5);
        secondAttribute.NamedSingle.ShouldBe(2.5f);

        session.Descriptors.TryGet(nameof(NonfiniteAttributeModel.Third), out var third).ShouldBeTrue();
        var thirdAttribute = third.Attributes.OfType<NonfiniteProbeAttribute>().Single();
        thirdAttribute.Value.ShouldBe(3.25);
        double.IsNaN(thirdAttribute.NamedDouble).ShouldBeTrue();
        float.IsNaN(thirdAttribute.NamedSingle).ShouldBeTrue();
    }

    [Test]
    public void GeneratedAttributesUseFieldReferencesAndCompile()
    {
        var tree = CSharpSyntaxTree.ParseText(ProbeSource, path: "Probe.cs");
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trustedAssemblies
            .Select(static assemblyPath =>
                (MetadataReference)MetadataReference.CreateFromFile(assemblyPath)
            )
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        references.Add(MetadataReference.CreateFromFile(typeof(NonfiniteProbeAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "NonfiniteProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        updated
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();

        var sources = driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Select(static generated => generated.SourceText.ToString())
            .ToImmutableArray();
        sources.Length.ShouldBeGreaterThan(0);
        var descriptors = string.Join("\n", sources);
        descriptors.ShouldContain("global::System.Double.NaN");
        descriptors.ShouldContain("global::System.Double.PositiveInfinity");
        descriptors.ShouldContain("global::System.Single.NegativeInfinity");
        // Finite literals keep their compact suffix form.
        descriptors.ShouldContain("1.5D");
        descriptors.ShouldNotContain("NaND");
        descriptors.ShouldNotContain("InfinityF");
        descriptors.ShouldNotContain("InfinityD");
    }
}
