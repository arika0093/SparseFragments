using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

public sealed class SparseEntityMapperEmitterTests
{
    private const string StubUsings = """
        using System;
        using System.Collections.Generic;
        using System.Linq.Expressions;
        """;

    private const string Stubs = """
        namespace SparseFragments.EntityFrameworkCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class SparseFragmentMapperAttribute : Attribute { }
            public interface IEntityModelMapping<TEntity, TDto>
            {
                void Configure(SparseEntityMapBuilder<TEntity, TDto> map);
            }
            public class SparseEntityMapBuilder<TEntity, TDto>
            {
                public SparseEntityMapBuilder<TEntity, TDto> Member<TDtoValue, TEntityValue>(
                    Expression<Func<TDto, TDtoValue?>> dto,
                    Expression<Func<TEntity, TEntityValue?>> entity) => this;
                public SparseEntityMapBuilder<TEntity, TDto> Object<TDtoChild, TEntityChild>(
                    Expression<Func<TDto, TDtoChild?>> dto,
                    Expression<Func<TEntity, TEntityChild?>> entity,
                    Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> configure)
                    where TDtoChild : class where TEntityChild : class => this;
                public SparseEntityMapBuilder<TEntity, TDto> DeclareCollection<TDtoValue, TEntityValue>(
                    Expression<Func<TDto, TDtoValue?>> dto,
                    Expression<Func<TEntity, TEntityValue?>> entity) => this;
            }
        }
        """;

    private static CSharpCompilation ProbeCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(StubUsings + source + Stubs);
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
            "SparseEntityMapperProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string Sources) RunDriver(string source)
    {
        var compilation = ProbeCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var sources = string.Join(
            "\n",
            runResult
                .Results.SelectMany(static r => r.GeneratedSources)
                .Where(static s => s.HintName.Contains("SparseEfDefaults"))
                .Select(static s => s.SourceText.ToString())
        );
        return (runResult.Diagnostics, sources);
    }

    private static string RenderDirect(string source, string mapperMetadataName)
    {
        var compilation = ProbeCompilation(source);
        var mapper = compilation.GetTypeByMetadataName(mapperMetadataName);
        mapper.ShouldNotBeNull($"probe mapper '{mapperMetadataName}' should resolve");
        var result = SparseEntityMapperEmitter.RenderMapper(mapper!, CancellationToken.None);
        var diagnostics = string.Join(
            ",",
            result.Diagnostics.Select(static d =>
                d.DescriptorId + ":" + d.Arguments.FirstOrDefault()
            )
        );
        (result.Source ?? string.Empty).ShouldNotBeEmpty(
            $"companion should emit (hint={result.HintName}, diagnostics=[{diagnostics}])"
        );
        return result.Source ?? string.Empty;
    }

    [Test]
    public void SameNameScalarsNestedAndCollectionsEmitDefaults()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            using System.Collections.Generic;
            namespace Probe
            {
                public sealed class AddressDto
                {
                    public string Street { get; set; } = string.Empty;
                    public string City { get; set; } = string.Empty;
                }
                public sealed class AddressEntity
                {
                    public string Street { get; set; } = string.Empty;
                    public string City { get; set; } = string.Empty;
                }
                public sealed class LineDto
                {
                    public int Qty { get; set; }
                }
                public sealed class LineEntity
                {
                    public int Qty { get; set; }
                }
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                    public string Renamed { get; set; } = string.Empty;
                    public AddressDto? Address { get; set; }
                    public List<LineDto> Lines { get; set; } = new();
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                    public string CustomerName { get; set; } = string.Empty;
                    public AddressEntity? Address { get; set; }
                    public List<LineEntity> Lines { get; set; } = new();
                }
                [SparseFragmentMapper]
                public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
                {
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map) { }
                }
            }
            """;
        var sources = RenderDirect(source, "Probe.OrderMapper");
        sources.ShouldContain(
            "builder.Member(static dto => dto.Notes, static entity => entity.Notes);"
        );
        sources.ShouldContain(
            "builder.Object(static dto => dto.Address, static entity => entity.Address, static child =>"
        );
        sources.ShouldContain(
            "child.Member(static dto => dto.Street, static entity => entity.Street);"
        );
        sources.ShouldContain(
            "child.Member(static dto => dto.City, static entity => entity.City);"
        );
        sources.ShouldContain(
            "builder.DeclareCollection(static dto => dto.Lines, static entity => entity.Lines);"
        );
        // Renamed/flattened pairs stay explicit-only: no guessed default.
        sources.ShouldNotContain("Renamed");
        sources.ShouldNotContain("CustomerName");

        // The full driver pipeline emits the same companion through discovery.
        var (diagnostics, driverSources) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        driverSources.ShouldContain(
            "builder.Member(static dto => dto.Notes, static entity => entity.Notes);"
        );
    }

    [Test]
    public void IncompatibleSameNamePairReportsSpf033()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Code { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public int Code { get; set; }
                }
                [SparseFragmentMapper]
                public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
                {
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map) { }
                }
            }
            """;
        var (diagnostics, sources) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF033");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("OrderDto.Code");
        match.Location.IsInSource.ShouldBeTrue();
        sources.ShouldNotContain("dto.Code");
    }

    [Test]
    public void MapperWithoutInterfaceReportsSpf034()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            namespace Probe
            {
                [SparseFragmentMapper]
                public sealed class OrderMapper
                {
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("OrderMapper");
    }
}
