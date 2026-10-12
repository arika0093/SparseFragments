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
            public enum SparseEntityMissingPolicy
            {
                Throw = 0,
                Ignore = 1,
                SetDefault = 2,
                SetNull = 3,
            }
            public class SparseEntityMapBuilder<TEntity, TDto>
            {
                public SparseEntityMapBuilder<TEntity, TDto> Member<TDtoValue, TEntityValue>(
                    Expression<Func<TDto, TDtoValue?>> dto,
                    Expression<Func<TEntity, TEntityValue?>> entity) => this;
                public SparseEntityMapBuilder<TEntity, TDto> Member<TDtoValue, TEntityValue>(
                    Expression<Func<TDto, TDtoValue?>> dto,
                    Expression<Func<TEntity, TEntityValue?>> entity,
                    Func<TDtoValue?, TEntityValue?> convert) => this;
                public SparseEntityMapBuilder<TEntity, TDto> Ignore<TValue>(
                    Expression<Func<TDto, TValue?>> dto) => this;
                public SparseEntityMapBuilder<TEntity, TDto> OnMissing<TValue>(
                    Expression<Func<TDto, TValue?>> dto,
                    SparseEntityMissingPolicy policy) => this;
                public SparseEntityMapBuilder<TEntity, TDto> AllowWrite<TValue>(
                    Expression<Func<TDto, TValue?>> dto) => this;
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
        // Path-scoped conventional element defaults ride alongside the marker.
        sources.ShouldContain("builder.DeclareCollectionElement<");
        sources.ShouldContain("LineDto,");
        sources.ShouldContain(
            "child.Member(static dto => dto.Qty, static entity => entity.Qty);"
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
    public void NullableValueMismatchReportsSpf033ButAnnotationsDoNot()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public int? Count { get; set; }
                    public string? Label { get; set; }
                }
                public sealed class OrderEntity
                {
                    public int Count { get; set; }
                    public string Label { get; set; } = string.Empty;
                }
                [SparseFragmentMapper]
                public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
                {
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map) { }
                }
            }
            """;
        var (diagnostics, sources) = RunDriver(source);
        // Nullable<T> never matches its underlying type (runtime exact-type
        // check parity), while reference-type annotations are ignored.
        var match = diagnostics.Single(static d => d.Id == "SPF033");
        match.GetMessage().ShouldContain("OrderDto.Count");
        sources.ShouldNotContain("dto.Count");
        sources.ShouldContain(
            "builder.Member(static dto => dto.Label, static entity => entity.Label);"
        );
    }

    [Test]
    public void InheritedMembersEmitDefaults()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            namespace Probe
            {
                public class BaseDto
                {
                    public string Base { get; set; } = string.Empty;
                }
                public sealed class OrderDto : BaseDto
                {
                    public string Own { get; set; } = string.Empty;
                }
                public class BaseEntity
                {
                    public string Base { get; set; } = string.Empty;
                }
                public sealed class OrderEntity : BaseEntity
                {
                    public string Own { get; set; } = string.Empty;
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
            "builder.Member(static dto => dto.Own, static entity => entity.Own);"
        );
        sources.ShouldContain(
            "builder.Member(static dto => dto.Base, static entity => entity.Base);"
        );
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Test]
    public void ExplicitConvertSuppressesSpf033()
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
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                        map.Member(dto => dto.Code, entity => entity.Code, code => code == null ? 0 : int.Parse(code));
                    }
                }
            }
            """;
        var (diagnostics, sources) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        sources.ShouldNotContain("dto.Code");
    }

    [Test]
    public void ExplicitIgnoreSuppressesSpf033()
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
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                        map.Ignore(dto => dto.Code);
                    }
                }
            }
            """;
        var (diagnostics, sources) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        sources.ShouldNotContain("dto.Code");
    }

    [Test]
    public void PolicyOnlyMemberStillReportsSpf033()
    {
        const string onMissingSource = """
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
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                        map.OnMissing(dto => dto.Code, SparseEntityMissingPolicy.Ignore);
                    }
                }
            }
            """;
        var (onMissingDiagnostics, _) = RunDriver(onMissingSource);
        onMissingDiagnostics
            .Single(static d => d.Id == "SPF033")
            .GetMessage()
            .ShouldContain("OrderDto.Code");

        const string allowWriteSource = """
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
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                        map.AllowWrite(dto => dto.Code);
                    }
                }
            }
            """;
        var (allowWriteDiagnostics, _) = RunDriver(allowWriteSource);
        allowWriteDiagnostics
            .Single(static d => d.Id == "SPF033")
            .GetMessage()
            .ShouldContain("OrderDto.Code");
    }

    [Test]
    public void ConverterlessMemberStillReportsSpf033()
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
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                        map.Member(dto => dto.Code, entity => entity.Code);
                    }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Single(static d => d.Id == "SPF033")
            .GetMessage()
            .ShouldContain("OrderDto.Code");
    }

    [Test]
    public void DynamicConfigureLeavesSpf033ToRuntime()
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
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                        Setup(map);
                    }
                    private static void Setup(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                        map.Ignore(dto => dto.Code);
                    }
                }
            }
            """;
        // Helper-delegated Configure bodies are outside the analyzable subset:
        // nothing is provable, so no SPF033 fires and runtime plan validation
        // remains the backstop.
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Test]
    public void KeywordMembersEscapeAndCompile()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string @event { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string @event { get; set; } = string.Empty;
                }
                [SparseFragmentMapper]
                public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
                {
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map) { }
                }
            }
            """;
        var (diagnostics, sources) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        emitted.ShouldContain("dto.@event");
        emitted.ShouldContain("entity.@event");

        // Compile fixture: the emitted companion must build with the probe.
        CompileEmitted(source, emitted);
    }

    [Test]
    public void DateOnlyPairEmitsDefaultAndCompiles()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public System.DateOnly Due { get; set; }
                }
                public sealed class OrderEntity
                {
                    public System.DateOnly Due { get; set; }
                }
                [SparseFragmentMapper]
                public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
                {
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map) { }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        emitted.ShouldContain(
            "builder.Member(static dto => dto.Due, static entity => entity.Due);"
        );
        CompileEmitted(source, emitted);
    }

    [Test]
    public void CustomStructPairReportsSpf035()
    {
        const string source = """
            using SparseFragments.EntityFrameworkCore;
            namespace Probe
            {
                public struct CustomValue
                {
                    public int X { get; set; }
                }
                public sealed class OrderDto
                {
                    public CustomValue Code { get; set; }
                }
                public sealed class OrderEntity
                {
                    public CustomValue Code { get; set; }
                }
                [SparseFragmentMapper]
                public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
                {
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map) { }
                }
            }
            """;
        var (diagnostics, sources) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF035");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("OrderDto.Code");
        sources.ShouldNotContain("dto.Code");
    }

    private static void CompileEmitted(string source, string emitted)
    {
        var tree = CSharpSyntaxTree.ParseText(StubUsings + source + Stubs + emitted);
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
            "SparseEntityMapperCompileProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        compilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
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
