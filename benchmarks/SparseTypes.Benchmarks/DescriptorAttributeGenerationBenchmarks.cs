using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes;
using SparseTypes.Generator;

/// <summary>Measures descriptor generation as attributed-member counts grow.</summary>
[MemoryDiagnoser]
public class DescriptorAttributeGenerationBenchmarks
{
    [Params(0, 10, 100, 500)]
    public int AttributedMembers { get; set; }

    [Params(false, true)]
    public bool ReuseAttributeExpression { get; set; }

    private CSharpCompilation _input = null!;
    private GeneratorDriver _repeatedDriver = null!;

    [GlobalSetup]
    public void Setup()
    {
        _input = CreateCompilation(AttributedMembers, ReuseAttributeExpression);
        _repeatedDriver = CSharpGeneratorDriver
            .Create(new SparseTypesGenerator())
            .RunGenerators(_input);
        var size = GeneratedSourceBytes(_repeatedDriver);
        Console.WriteLine(
            "Descriptor attribute source size: "
                + AttributedMembers
                + " members, repeated expression: "
                + ReuseAttributeExpression
                + ", "
                + size
                + " bytes"
        );
    }

    [Benchmark(Description = "Descriptor generation cold")]
    public int ColdGenerationSourceBytes()
    {
        var driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator()).RunGenerators(_input);
        return GeneratedSourceBytes(driver);
    }

    [Benchmark(Description = "Descriptor generation repeated")]
    public int RepeatedGenerationSourceBytes()
    {
        _repeatedDriver = _repeatedDriver.RunGenerators(_input);
        return GeneratedSourceBytes(_repeatedDriver);
    }

    private static CSharpCompilation CreateCompilation(int count, bool repeatedExpression)
    {
        var source = new StringBuilder(
            """
            using System;
            using SparseTypes;
            namespace DescriptorAttributeBenchmarks;
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class ProbeAttribute : Attribute
            {
                public ProbeAttribute(string value) { }
            }
            [SparseModel]
            public partial class DescriptorModel
            {
            """
        );
        for (var index = 0; index < count; index++)
        {
            var value = repeatedExpression ? "shared-expression" : "member-" + index;
            source.Append("[ProbeAttribute(\"").Append(value).AppendLine("\")]");
            source.Append("public int Member").Append(index).AppendLine(" { get; set; }");
        }
        source.AppendLine("}");

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(
                MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
            );
        return CSharpCompilation.Create(
            "DescriptorAttributeBenchmarks",
            [CSharpSyntaxTree.ParseText(source.ToString())],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static int GeneratedSourceBytes(GeneratorDriver driver)
    {
        var result = driver.GetRunResult().Results.Single();
        if (result.Exception is not null)
        {
            throw new InvalidOperationException("Descriptor generation failed.", result.Exception);
        }

        var errors = driver
            .GetRunResult()
            .Diagnostics.Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
            );
        if (errors.Any())
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        return result.GeneratedSources.Sum(static source =>
            Encoding.UTF8.GetByteCount(source.SourceText.ToString())
        );
    }
}
