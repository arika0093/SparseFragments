using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Builder-scope isolation for the generated mapper bridge.</summary>
/// <remarks>
/// The generated <c>Map</c> member is backed by the builder active for the
/// current plan build only. These tests prove that repeated and overlapping
/// <c>Create</c> calls on one (singleton) mapping instance cannot mix, retain,
/// or corrupt scopes, that failures clear the scope, and that out-of-context
/// access fails deterministically.
/// </remarks>
public sealed partial class MapperScopeIsolationTests
{
    /// <summary>DTO with one same-name scalar for the defaults-only fixture.</summary>
    public sealed class ScopeFlatDto
    {
        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Entity with one same-name scalar for the defaults-only fixture.</summary>
    public sealed class ScopeFlatEntity
    {
        /// <summary>Gets or sets the identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Context for the defaults-only fixture.</summary>
    public sealed class ScopeFlatContext(DbContextOptions<ScopeFlatContext> options)
        : DbContext(options)
    {
        /// <summary>Gets the items.</summary>
        public DbSet<ScopeFlatEntity> Items => Set<ScopeFlatEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ScopeFlatEntity>(entity => entity.HasKey(item => item.Id));
        }
    }

    /// <summary>Conventions-only mapping without any Configure method.</summary>
    [SparseFragmentMapper<ScopeFlatEntity, ScopeFlatDto>]
    public sealed partial class ScopeFlatMapper { }

    /// <summary>Widget mapping that fails while the flag is set.</summary>
    [SparseFragmentMapper<WidgetEntity, WidgetDto>]
    public sealed partial class FlakyWidgetMapper
    {
        /// <summary>Gets or sets whether Configure throws.</summary>
        public bool Fail { get; set; } = true;

        private void Configure()
        {
            if (Fail)
            {
                throw new InvalidOperationException("Flaky Configure boom.");
            }

            Map.Ignore(dto => dto.Id);
            Map.Ignore(dto => dto.RowVersion);
            Map.Ignore(dto => dto.Display);
        }
    }

    /// <summary>Records concurrent Configure entries for serialization checks.</summary>
    public sealed class BuildContention
    {
        private int _active;
        private int _maxActive;

        /// <summary>Gets the maximum observed concurrent entries.</summary>
        public int MaxActive => Volatile.Read(ref _maxActive);

        /// <summary>Enters Configure, tracking overlap.</summary>
        public void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            try
            {
                int observed;
                do
                {
                    observed = Volatile.Read(ref _maxActive);
                } while (
                    active > observed
                    && Interlocked.CompareExchange(ref _maxActive, active, observed) != observed
                );
                Thread.Sleep(20);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    /// <summary>Widget mapping reporting Configure entries to shared contention state.</summary>
    [SparseFragmentMapper<WidgetEntity, WidgetDto>]
    public sealed partial class ContentionWidgetMapper
    {
        private readonly BuildContention _contention;

        /// <summary>Creates the mapping with shared contention state.</summary>
        /// <param name="contention">The shared contention recorder.</param>
        public ContentionWidgetMapper(BuildContention contention)
        {
            _contention = contention;
        }

        private void Configure()
        {
            _contention.Enter();
            Map.Ignore(dto => dto.Id);
            Map.Ignore(dto => dto.RowVersion);
            Map.Ignore(dto => dto.Display);
        }
    }

    [Test]
    public void DefaultsOnlyMapperBuildsWithoutConfigure()
    {
        using var connection = TestDatabases.OpenMemory();
        var options = new DbContextOptionsBuilder<ScopeFlatContext>().UseSqlite(connection).Options;
        using var context = new ScopeFlatContext(options);

        var mapper = SparseEntityMapper<ScopeFlatEntity, ScopeFlatDto>.Create(
            new ScopeFlatMapper(),
            context.Model
        );
        var dto = mapper.ToDto(new ScopeFlatEntity { Name = "plain" });
        dto.Name.ShouldBe("plain");
    }

    [Test]
    public void RepeatedBuildsOnSameInstanceStayIsolated()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var mapping = new WidgetMapper();

        var first = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(mapping, context.Model);
        var second = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(mapping, context.Model);

        first.ToDto(new WidgetEntity("first", "active")).Name.ShouldBe("first");
        second.ToDto(new WidgetEntity("second", "active")).Name.ShouldBe("second");
    }

    [Test]
    public void ConcurrentBuildsOnSingletonInstanceStayIsolated()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var mapping = new WidgetMapper();
        var mappers = new SparseEntityMapper<WidgetEntity, WidgetDto>[16];

        Parallel.For(
            0,
            mappers.Length,
            index =>
            {
                mappers[index] = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
                    mapping,
                    context.Model
                );
            }
        );

        for (var index = 0; index < mappers.Length; index++)
        {
            mappers[index]
                .ToDto(new WidgetEntity("name-" + index, "active"))
                .Name.ShouldBe("name-" + index);
        }
    }

    [Test]
    public void OverlappingBuildsSerializeOnSingletonInstance()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var contention = new BuildContention();
        var mapping = new ContentionWidgetMapper(contention);

        Parallel.For(
            0,
            8,
            _ =>
            {
                SparseEntityMapper<WidgetEntity, WidgetDto>.Create(mapping, context.Model);
            }
        );

        contention.MaxActive.ShouldBe(1);
    }

    [Test]
    public void ConfigureFailurePropagatesAndLaterBuildsRecover()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var mapping = new FlakyWidgetMapper();

        var error = Should.Throw<InvalidOperationException>(() =>
            SparseEntityMapper<WidgetEntity, WidgetDto>.Create(mapping, context.Model)
        );
        error.Message.ShouldContain("Flaky Configure boom.");

        mapping.Fail = false;
        var mapper = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(mapping, context.Model);
        mapper.ToDto(new WidgetEntity("recovered", "active")).Name.ShouldBe("recovered");
    }

    [Test]
    public void MapOutsideConfigureFailsDeterministically()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var mapping = new WidgetMapper();
        _ = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(mapping, context.Model);

        var map = typeof(WidgetMapper).GetProperty(
            "Map",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        map.ShouldNotBeNull();

        var error = Should.Throw<TargetInvocationException>(() => map!.GetValue(mapping, null));
        error.InnerException.ShouldBeOfType<InvalidOperationException>();
        error.InnerException!.Message.ShouldContain("only during Configure");
    }
}
