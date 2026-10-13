using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

public sealed class SparseEntityMapperEmitterTests
{
    private const string StubUsings = """
        using System;
        using System.Collections.Generic;
        using System.Linq.Expressions;
        """;

    private const string Stubs = """
        namespace SparseTypes.EntityFrameworkCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class SparseFragmentMapperAttribute<TEntity, TDto> : Attribute
                where TEntity : class where TDto : class
            {
                public Type EntityType => typeof(TEntity);
                public Type DtoType => typeof(TDto);
            }
            public sealed class SparseEntityMapper<TEntity, TDto>
                where TEntity : class where TDto : class
            {
                public static SparseEntityMapper<TEntity, TDto> Create<TMapping>(
                    TMapping mapping, global::Microsoft.EntityFrameworkCore.Metadata.IModel model)
                    where TMapping : IEntityModelMapping<TEntity, TDto>
                    => new SparseEntityMapper<TEntity, TDto>();
            }
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
                public SparseEntityMapBuilder<TEntity, TDto> DeclareCollectionElement<
                    TDtoChild, TEntityChild, TKey>(
                    Expression<Func<TDto, IEnumerable<TDtoChild>?>> dto,
                    Expression<Func<TDtoChild, TKey>> key,
                    object? unassignedKeyValue,
                    bool hasUnassignedKeyValue,
                    Expression<Func<TDtoChild, Guid?>>? temporaryKey,
                    Action<TDtoChild, Guid?>? temporaryKeySetter,
                    string[] keyMemberNames,
                    Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> configure)
                    where TDtoChild : class where TEntityChild : class => this;
            }
        }
        namespace SparseTypes.EntityFrameworkCore.Mapping
        {
            public static class GeneratedDefaultsRegistry
            {
                public static void Register<TEntity, TDto>(
                    Type mapperType,
                    Action<SparseTypes.EntityFrameworkCore.SparseEntityMapBuilder<TEntity, TDto>> registerDefaults)
                    where TEntity : class where TDto : class { }
            }
        }
        namespace Microsoft.EntityFrameworkCore.Metadata
        {
            public interface IModel { }
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
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
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
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
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
            using SparseTypes.EntityFrameworkCore;
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
                    [SparseTypes.SparseKey(Unassigned = 0)]
                    public int Id { get; set; }
                    [SparseTypes.SparseTemporaryKey]
                    public Guid? TemporaryId { get; set; }
                    public int Qty { get; set; }
                }
                public sealed class LineEntity
                {
                    public int Id { get; set; }
                    public Guid? TemporaryId { get; set; }
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
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
        sources.ShouldContain("static __key => __key.TemporaryId");
        sources.ShouldContain("static (__dto, __value) => __dto.TemporaryId = __value");
        sources.ShouldContain("child.Member(static dto => dto.Qty, static entity => entity.Qty);");
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
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
    public void NullableSparseKeyMemberIsIgnoredByGeneratedElementDefaults()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            using System.Collections.Generic;
            namespace Probe
            {
                public sealed class LineDto
                {
                    public int? Id { get; set; }
                    [SparseTypes.SparseKey(Unassigned = 0)]
                    public int Key => Id ?? 0;
                    public int Qty { get; set; }
                }
                public sealed class LineEntity
                {
                    public int Id { get; set; }
                    public int Qty { get; set; }
                }
                public sealed class OrderDto
                {
                    public List<LineDto> Lines { get; set; } = new();
                }
                public sealed class OrderEntity
                {
                    public List<LineEntity> Lines { get; set; } = new();
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
                }
            }
            """;
        var (diagnostics, sources) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        sources.ShouldContain("child.Ignore(static dto => dto.Id);");
        sources.ShouldContain("child.Member(static dto => dto.Qty, static entity => entity.Qty);");
        sources.ShouldNotContain("dto => dto.Id, static entity => entity.Id");
    }

    [Test]
    public void InheritedMembersEmitDefaults()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                        Map.Member(dto => dto.Code, entity => entity.Code, code => code == null ? 0 : int.Parse(code));
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                        Map.Ignore(dto => dto.Code);
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                        Map.OnMissing(dto => dto.Code, SparseEntityMissingPolicy.Ignore);
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                        Map.AllowWrite(dto => dto.Code);
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                        Map.Member(dto => dto.Code, entity => entity.Code);
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                        Setup(Map);
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
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
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
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
    public void InitOnlyTemporaryKeyEmitsNullSetterWithDiagnosticAndCompiles()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            using System.Collections.Generic;
            namespace Probe
            {
                public sealed class LineDto
                {
                    [SparseTypes.SparseKey(Unassigned = 0)]
                    public int Id { get; set; }
                    [SparseTypes.SparseTemporaryKey]
                    public Guid? TemporaryId { get; init; }
                    public int Qty { get; set; }
                }
                public sealed class LineEntity
                {
                    public int Id { get; set; }
                    public Guid? TemporaryId { get; set; }
                    public int Qty { get; set; }
                }
                public sealed class OrderDto
                {
                    public List<LineDto> Lines { get; set; } = new();
                }
                public sealed class OrderEntity
                {
                    public List<LineEntity> Lines { get; set; } = new();
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF035");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("TemporaryId");
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        // Correlation still reads the temporary identity; only the
        // post-construction assignment degrades to null so the companion builds.
        emitted.ShouldContain("static __key => __key.TemporaryId");
        emitted.ShouldContain("child.Ignore(static dto => dto.TemporaryId);");
        emitted.ShouldNotContain("__dto.TemporaryId = __value");

        // Compile fixture: the emitted companion must build with the probe.
        CompileEmitted(source, emitted);
    }

    [Test]
    public void PrivateDtoMemberIsSkippedAndCompiles()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                    private string Secret { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                    public string Secret { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        emitted.ShouldContain(
            "builder.Member(static dto => dto.Notes, static entity => entity.Notes);"
        );
        emitted.ShouldNotContain("Secret");

        // Compile fixture: the emitted companion must build with the probe.
        CompileEmitted(source, emitted);
    }

    [Test]
    public void PrivateEntityMemberReportsSpf035AndCompiles()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                    public string Secret { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                    private string Secret { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF035");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("Secret");
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        emitted.ShouldContain(
            "builder.Member(static dto => dto.Notes, static entity => entity.Notes);"
        );
        emitted.ShouldNotContain("entity.Secret");

        // Compile fixture: the emitted companion must build with the probe.
        CompileEmitted(source, emitted);
    }

    [Test]
    public void InaccessibleAndReadonlyShapesCompileWithoutInvalidSelectors()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public class BaseDto
                {
                    public string Base { get; set; } = string.Empty;
                }
                public sealed class AddressDto
                {
                    public string Street { get; set; } = string.Empty;
                }
                public sealed class OrderDto : BaseDto
                {
                    public string Notes { get; set; } = string.Empty;
                    public string Label { get; init; } = string.Empty;
                    public string Computed => Notes;
                    internal string InternalNote { get; set; } = string.Empty;
                    protected string Guarded { get; set; } = string.Empty;
                    private string Secret { get; set; } = string.Empty;
                    public AddressDto? Address { get; set; }
                }
                public class BaseEntity
                {
                    public string Base { get; set; } = string.Empty;
                }
                public sealed class AddressEntity
                {
                    public string Street { get; set; } = string.Empty;
                }
                public sealed class OrderEntity : BaseEntity
                {
                    public string Notes { get; set; } = string.Empty;
                    public string Label { get; set; } = string.Empty;
                    public string InternalNote { get; set; } = string.Empty;
                    public string Guarded { get; set; } = string.Empty;
                    public string Secret { get; set; } = string.Empty;
                    public AddressEntity? Address { get; set; }
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        // Public, inherited, init-only (read-only selectors) and same-assembly
        // internal members keep their defaults, including nested objects.
        emitted.ShouldContain(
            "builder.Member(static dto => dto.Notes, static entity => entity.Notes);"
        );
        emitted.ShouldContain(
            "builder.Member(static dto => dto.Base, static entity => entity.Base);"
        );
        emitted.ShouldContain(
            "builder.Member(static dto => dto.Label, static entity => entity.Label);"
        );
        emitted.ShouldContain(
            "builder.Member(static dto => dto.InternalNote, static entity => entity.InternalNote);"
        );
        emitted.ShouldContain(
            "builder.Object(static dto => dto.Address, static entity => entity.Address, static child =>"
        );
        // Protected/private DTO members and getter-only computed members stay
        // unmapped without invalid selectors.
        emitted.ShouldNotContain("Guarded");
        emitted.ShouldNotContain("Secret");
        emitted.ShouldNotContain("dto.Computed");

        // Compile fixture: the emitted companion must build with the probe.
        CompileEmitted(source, emitted);
    }

    [Test]
    public void CustomStructPairReportsSpf035()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
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
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "SparseEntityMapperCompileProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        var errors = compilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        compilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    [Test]
    public void NonPartialMapperReportsSpf034()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                [SparseFragmentMapper<object, object>]
                public sealed class OrderMapper
                {
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("OrderMapper");
        match.GetMessage().ShouldContain("partial");
        match.Location.IsInSource.ShouldBeTrue();
    }

    [Test]
    public void ManualInterfaceImplReportsSpf034()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
                {
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("must not implement IEntityModelMapping manually");
    }

    [Test]
    public void MapCollisionReportsSpf034()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    public string Map { get; set; } = string.Empty;
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("'Map'");
    }

    [Test]
    public void LegacyConfigureSignatureReportsSpf034()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
                    {
                    }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("unsupported Configure signature");
    }

    [Test]
    public void DuplicateConfigureReportsSpf034()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                    }
                    public void Configure(int unused)
                    {
                    }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("Configure");
    }

    [Test]
    public void GenericMapperReportsSpf034()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper<T>
                {
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("must not be generic");
    }

    [Test]
    public void NestedMapperUnderNonPartialReportsSpf034()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class Outer
                {
                    [SparseFragmentMapper<OrderEntity, OrderDto>]
                    public sealed partial class OrderMapper
                    {
                    }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        var match = diagnostics.Single(static d => d.Id == "SPF034");
        match.Severity.ShouldBe(DiagnosticSeverity.Error);
        match.GetMessage().ShouldContain("partial");
    }

    [Test]
    public void DefaultsOnlyMapperEmitsWithoutConfigure()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        emitted.ShouldContain(
            "builder.Member(static dto => dto.Notes, static entity => entity.Notes);"
        );
        // The bridge keeps the defaults-only scope inert without calling Configure.
        emitted.ShouldContain("_ = builder;");
        emitted.ShouldNotContain("Configure();");
        CompileEmitted(source, emitted);
    }

    [Test]
    public void BridgeImplementsMappingWithMapScopeAndCompiles()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
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
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure()
                    {
                        Map.Ignore(dto => dto.Code);
                    }
                }
            }
            """;
        var (diagnostics, _) = RunDriver(source);
        diagnostics
            .Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var emitted = RenderDirect(source, "Probe.OrderMapper");
        emitted.ShouldContain(
            "IEntityModelMapping<global::Probe.OrderEntity, global::Probe.OrderDto>.Configure"
        );
        emitted.ShouldContain("__sparse_map_scope");
        emitted.ShouldContain("lock (__sparse_map_lock)");
        emitted.ShouldContain("Configure();");
        emitted.ShouldContain("__sparse_map_scope = null;");
        emitted.ShouldContain("exposes Map only during Configure");
        emitted.ShouldContain("DynamicDependency");
        CompileEmitted(source, emitted);
    }

    [Test]
    public void PartialMapperEmitsCreateShorthand()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    private void Configure() { }
                }
            }
            """;
        var sources = RenderDirect(source, "Probe.OrderMapper");
        sources.ShouldContain("partial class OrderMapper");
        sources.ShouldContain(
            "public static global::SparseTypes.EntityFrameworkCore.SparseEntityMapper<global::Probe.OrderEntity, global::Probe.OrderDto> Create(global::Microsoft.EntityFrameworkCore.Metadata.IModel model)"
        );
        sources.ShouldContain("var mapping = new OrderMapper();");
        // The generated bridge owns the mapping contract and Map scope.
        sources.ShouldContain(
            "IEntityModelMapping<global::Probe.OrderEntity, global::Probe.OrderDto>"
        );
        sources.ShouldContain(
            "private global::SparseTypes.EntityFrameworkCore.SparseEntityMapBuilder<global::Probe.OrderEntity, global::Probe.OrderDto> Map"
        );
    }

    [Test]
    public void InjectedMapperOmitsCreateShorthand()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            namespace Probe
            {
                public sealed class OrderDto
                {
                    public string Notes { get; set; } = string.Empty;
                }
                public sealed class OrderEntity
                {
                    public string Notes { get; set; } = string.Empty;
                }
                [SparseFragmentMapper<OrderEntity, OrderDto>]
                public sealed partial class OrderMapper
                {
                    public OrderMapper(string name) { }
                    private void Configure() { }
                }
            }
            """;
        var sources = RenderDirect(source, "Probe.OrderMapper");
        sources.ShouldNotContain("SparseEntityMapper<");
    }

    [Test]
    public void EmittedFactoryCompiles()
    {
        const string source = """
            using SparseTypes.EntityFrameworkCore;
            using System.Collections.Generic;
            namespace Probe
            {
                public sealed class PlaygroundOrderLineDto
                {
                    public int? Id { get; set; }
                    [SparseTypes.SparseKey(Unassigned = 0)]
                    public int Key => Id ?? 0;
                    [SparseTypes.SparseTemporaryKey]
                    public Guid? TemporaryId { get; set; }
                    public string Product { get; set; } = string.Empty;
                    public int Qty { get; set; }
                }
                public sealed class PlaygroundOrderDto
                {
                    public string Customer { get; set; } = string.Empty;
                    public string Notes { get; set; } = string.Empty;
                    public decimal Total { get; set; }
                    public List<PlaygroundOrderLineDto> Lines { get; set; } = new();
                }
                public sealed class PlaygroundNoteEntity
                {
                    public int Id { get; set; }
                    public string Text { get; set; } = string.Empty;
                    public int OrderId { get; set; }
                }
                public sealed class PlaygroundOrderLineEntity
                {
                    public int Id { get; set; }
                    public string Product { get; set; } = string.Empty;
                    public int Qty { get; set; }
                    public int SortOrder { get; set; }
                    public int OrderId { get; set; }
                }
                public sealed class PlaygroundOrderEntity
                {
                    public int Id { get; set; }
                    public int Revision { get; set; }
                    public string CustomerName { get; set; } = string.Empty;
                    public decimal Total { get; set; }
                    public PlaygroundNoteEntity? Note { get; set; }
                    public List<PlaygroundOrderLineEntity> Lines { get; set; } = new();
                }
                [SparseFragmentMapper<PlaygroundOrderEntity, PlaygroundOrderDto>]
                public sealed partial class PlaygroundOrderMapper
                {
                    private void Configure()
                    {
                    }
                }
            }
            """;
        var sources = RenderDirect(source, "Probe.PlaygroundOrderMapper");
        sources.ShouldContain(
            "> Create(global::Microsoft.EntityFrameworkCore.Metadata.IModel model)"
        );
        CompileEmitted(source, sources);
    }
}
