using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SparseTypes.EntityFrameworkCore.Tests.Concurrency;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Counts executed database commands to prove Apply issues none.</summary>
public sealed class QueryCounter : DbCommandInterceptor
{
    private int _commands;

    /// <summary>Gets the number of commands executed so far.</summary>
    public int Commands => _commands;

    /// <summary>Resets the counter.</summary>
    public void Reset() => Interlocked.Exchange(ref _commands, 0);

    private void Count() => Interlocked.Increment(ref _commands);

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Count();
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Count();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Count();
        return base.NonQueryExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Count();
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        Count();
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Count();
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>SQLite in-memory context for the mapping test models.</summary>
public sealed class MappingTestContext : DbContext
{
    public MappingTestContext(DbContextOptions<MappingTestContext> options)
        : base(options) { }

    public DbSet<WidgetEntity> Widgets => Set<WidgetEntity>();

    public DbSet<GadgetEntity> Gadgets => Set<GadgetEntity>();

    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

    public DbSet<CartEntity> Carts => Set<CartEntity>();

    public DbSet<TagsEntity> Tags => Set<TagsEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WidgetEntity>(entity =>
        {
            entity.HasKey(widget => widget.Id);
            entity.Property(widget => widget.RowVersion).IsConcurrencyToken();
            entity
                .Property(widget => widget.Status)
                .HasField("_status")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Property<string>("TenantId");
        });
        modelBuilder.Entity<DeviceEntity>(entity =>
        {
            entity.HasKey(device => device.Id);
            entity.HasAlternateKey(device => device.Serial);
        });
    }
}

/// <summary>Same entity with a deliberately different model, for per-model plan caching.</summary>
public sealed class MappingVariantContext : DbContext
{
    public MappingVariantContext(DbContextOptions<MappingVariantContext> options)
        : base(options) { }

    public DbSet<WidgetEntity> Widgets => Set<WidgetEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WidgetEntity>(entity =>
        {
            entity.HasKey(widget => widget.Id);
            entity.Property(widget => widget.RowVersion).IsConcurrencyToken();
            entity
                .Property(widget => widget.Status)
                .HasField("_status")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Property<string>("TenantId");
            entity.Property<string>("ExtraShadow");
        });
    }
}

/// <summary>Opens shared SQLite in-memory connections for tests.</summary>
public static class TestDatabases
{
    /// <summary>Creates an open in-memory connection.</summary>
    public static SqliteConnection OpenMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static TContext Create<TContext>(
        SqliteConnection connection,
        QueryCounter counter,
        Func<DbContextOptions<TContext>, TContext> factory
    )
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseSqlite(connection);
        builder.AddInterceptors(counter);
        var context = factory(builder.Options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>Creates a sample context over an open connection.</summary>
    public static SampleDbContext CreateSample(SqliteConnection connection, QueryCounter counter)
    {
        return Create(
            connection,
            counter,
            (DbContextOptions<SampleDbContext> options) => new SampleDbContext(options)
        );
    }

    /// <summary>Creates a versioned-orders context over an open connection.</summary>
    public static VersionedOrdersContext CreateVersioned(
        SqliteConnection connection,
        QueryCounter counter
    )
    {
        return Create(
            connection,
            counter,
            (DbContextOptions<VersionedOrdersContext> options) =>
                new VersionedOrdersContext(options)
        );
    }

    /// <summary>Creates a mapping-test context over an open connection.</summary>
    public static MappingTestContext CreateMapping(
        SqliteConnection connection,
        QueryCounter counter
    )
    {
        return Create(
            connection,
            counter,
            (DbContextOptions<MappingTestContext> options) => new MappingTestContext(options)
        );
    }

    /// <summary>Creates a variant mapping-test context over an open connection.</summary>
    public static MappingVariantContext CreateVariant(
        SqliteConnection connection,
        QueryCounter counter
    )
    {
        return Create(
            connection,
            counter,
            (DbContextOptions<MappingVariantContext> options) => new MappingVariantContext(options)
        );
    }

    /// <summary>Creates a keyed mapping-test context over an open connection.</summary>
    public static KeyedMappingContext CreateKeyed(SqliteConnection connection, QueryCounter counter)
    {
        return Create(
            connection,
            counter,
            (DbContextOptions<KeyedMappingContext> options) => new KeyedMappingContext(options)
        );
    }
}
