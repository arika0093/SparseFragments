using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>DI registration and per-model plan caching tests.</summary>
public sealed class DiAndPlanCacheTests
{
    /// <summary>Counts mapper configuration calls made while plans are built.</summary>
    public sealed class PlanBuildProbe
    {
        private int _configurationCount;

        /// <summary>Gets the number of mapping configurations executed.</summary>
        public int ConfigurationCount => Volatile.Read(ref _configurationCount);

        /// <summary>Records one mapping configuration.</summary>
        public void RecordConfiguration() => Interlocked.Increment(ref _configurationCount);
    }

    /// <summary>A widget mapping whose constructor dependency proves DI activation.</summary>
    [SparseFragmentMapper]
    public sealed class DiWidgetMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
    {
        private readonly PlanBuildProbe _probe;

        /// <summary>Creates the mapping with its DI-provided configuration probe.</summary>
        public DiWidgetMapper(PlanBuildProbe probe)
        {
            _probe = probe;
        }

        /// <summary>Gets the injected configuration probe.</summary>
        public PlanBuildProbe Probe => _probe;

        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
        {
            _probe.RecordConfiguration();
            map.Ignore(dto => dto.Id);
            map.Read(dto => dto.Id, entity => entity.Id);
            map.Ignore(dto => dto.RowVersion);
            map.Ignore(dto => dto.Display);
        }
    }

    [Test]
    public void DiMapperAppliesChangesAndBuildsOnlyOnePlanForTheSameModel()
    {
        using var connection = TestDatabases.OpenMemory();
        var probe = new PlanBuildProbe();
        using var provider = CreateProvider(connection, probe);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<MappingTestContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<MappingTestContext>();
        ReferenceEquals(firstContext.Model, secondContext.Model).ShouldBeTrue();

        var mapper = firstScope.ServiceProvider.GetRequiredService<
            ISparseEntityMapper<WidgetEntity, WidgetDto>
        >();
        var firstEntity = new WidgetEntity("before", "active") { Id = 17 };
        mapper.Apply(firstContext, firstEntity, ChangesTo("after"));
        firstEntity.Name.ShouldBe("after");
        var firstDto = mapper.ToDto(firstContext, firstEntity);
        firstDto.Name.ShouldBe("after");
        firstDto.Id.ShouldBe(17);

        var secondEntity = new WidgetEntity("second", "active");
        var result = mapper.ApplyWithResult(
            secondContext,
            secondEntity,
            ChangesTo("updated second")
        );
        result.HasCreatedEntities.ShouldBeFalse();
        var authoritative = mapper.ToDto(secondContext, secondEntity);
        mapper.AssignTemporaryKeys(
            secondContext,
            result,
            new WidgetDto { Name = "second" },
            authoritative
        );
        authoritative.Name.ShouldBe("updated second");
        probe.ConfigurationCount.ShouldBe(1);
    }

    [Test]
    public void DiMapperBuildsAnIsolatedPlanForEachModel()
    {
        using var connection = TestDatabases.OpenMemory();
        var probe = new PlanBuildProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddDbContext<MappingTestContext>(options => options.UseSqlite(connection));
        services.AddDbContext<MappingVariantContext>(options => options.UseSqlite(connection));
        services.AddSparseEntityMappers(typeof(DiWidgetMapper));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mappingContext = scope.ServiceProvider.GetRequiredService<MappingTestContext>();
        var variantContext = scope.ServiceProvider.GetRequiredService<MappingVariantContext>();
        ReferenceEquals(mappingContext.Model, variantContext.Model).ShouldBeFalse();

        var mapper = scope.ServiceProvider.GetRequiredService<
            ISparseEntityMapper<WidgetEntity, WidgetDto>
        >();
        mapper.Apply(mappingContext, new WidgetEntity("first", "active"), EmptyChanges());
        mapper.Apply(variantContext, new WidgetEntity("second", "active"), EmptyChanges());

        probe.ConfigurationCount.ShouldBe(2);
    }

    [Test]
    public void MappingTypesAreCreatedByDiWithConstructorInjection()
    {
        using var connection = TestDatabases.OpenMemory();
        var probe = new PlanBuildProbe();
        using var provider = CreateProvider(connection, probe);

        var mapping = provider.GetRequiredService<DiWidgetMapper>();
        mapping.Probe.ShouldBeSameAs(probe);
        var mapper = provider.GetRequiredService<ISparseEntityMapper<WidgetEntity, WidgetDto>>();
        using var scope = provider.CreateScope();
        mapper.Apply(
            scope.ServiceProvider.GetRequiredService<MappingTestContext>(),
            new WidgetEntity("before", "active"),
            EmptyChanges()
        );

        probe.ConfigurationCount.ShouldBe(1);
    }

    [Test]
    public void StartupValidationFailsForInvalidMappingsInTheContextModel()
    {
        using var connection = TestDatabases.OpenMemory();
        var services = new ServiceCollection();
        services.AddDbContext<MappingTestContext>(options => options.UseSqlite(connection));
        services.AddSparseEntityMappers(typeof(MappingPolicyTests.ValidationErrorsMapper));
        using var provider = services.BuildServiceProvider();

        var error = Should.Throw<SparseEntityMappingException>(() =>
            provider.ValidateSparseEntityMappers<MappingTestContext>()
        );

        error.Errors.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void StartupValidationSkipsMappingsOutsideTheContextModel()
    {
        using var connection = TestDatabases.OpenMemory();
        var services = new ServiceCollection();
        services.AddSingleton(new PlanBuildProbe());
        services.AddDbContext<MappingVariantContext>(options => options.UseSqlite(connection));
        services.AddSparseEntityMappers(typeof(DiWidgetMapper), typeof(GadgetAmbiguousMapper));
        using var provider = services.BuildServiceProvider();

        provider.ValidateSparseEntityMappers<MappingVariantContext>();
    }

    private static ServiceProvider CreateProvider(SqliteConnection connection, PlanBuildProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddDbContext<MappingTestContext>(options => options.UseSqlite(connection));
        services.AddSparseEntityMappers(typeof(DiWidgetMapper));
        return services.BuildServiceProvider();
    }

    private static WidgetDto.ChangeSet ChangesTo(string name) =>
        new WidgetDto { Name = "before" }.CreateChangeSet(new WidgetDto { Name = name });

    private static WidgetDto.ChangeSet EmptyChanges() =>
        new WidgetDto().CreateChangeSet(new WidgetDto());
}
