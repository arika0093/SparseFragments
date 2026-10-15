using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests.Concurrency;

/// <summary>Order DTO carrying the application-managed concurrency token.</summary>
[SparseModel]
public partial class VersionedOrderDto
{
    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Guid Version { get; set; }

    public List<VersionedOrderLineDto> Lines { get; set; } = [];
}

/// <summary>Order line DTO with key and temporary identity.</summary>
public partial class VersionedOrderLineDto
{
    public int Id { get; set; }

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Subtotal { get; set; }
}

/// <summary>Order entity guarded by an application-managed token.</summary>
public sealed class VersionedOrderEntity
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Guid Version { get; set; }

    public List<VersionedOrderLineEntity> Lines { get; set; } = [];
}

/// <summary>Order line entity belonging to one versioned aggregate.</summary>
public sealed class VersionedOrderLineEntity
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public int SortOrder { get; set; }

    public VersionedOrderEntity? Order { get; set; }
}

/// <summary>
/// SQLite context mirroring the EfCoreSample strategy: SQLite cannot
/// store-generate a rowversion, so the context assigns a new token on every
/// order or order-line write, including child-only mutations.
/// </summary>
public sealed class VersionedOrdersContext(DbContextOptions<VersionedOrdersContext> options)
    : DbContext(options)
{
    public DbSet<VersionedOrderEntity> Orders => Set<VersionedOrderEntity>();

    /// <inheritdoc />
    public override int SaveChanges()
    {
        AdvanceConcurrencyTokens();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AdvanceConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void AdvanceConcurrencyTokens()
    {
        var parents = new HashSet<int>();
        foreach (var line in ChangeTracker.Entries<VersionedOrderLineEntity>().ToList())
        {
            if (
                line.State
                is not EntityState.Added
                    and not EntityState.Modified
                    and not EntityState.Deleted
            )
            {
                continue;
            }

            var parent =
                line.Entity.Order
                ?? ChangeTracker
                    .Entries<VersionedOrderEntity>()
                    .Select(static entry => entry.Entity)
                    .FirstOrDefault(order => order.Id == line.Entity.OrderId);
            if (parent is not null)
            {
                parents.Add(parent.Id);
            }
        }

        foreach (var entry in ChangeTracker.Entries<VersionedOrderEntity>().ToList())
        {
            if (
                entry.State is EntityState.Added or EntityState.Modified
                || parents.Contains(entry.Entity.Id)
            )
            {
                entry.Entity.Version = Guid.NewGuid();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VersionedOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Version).IsConcurrencyToken();
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<VersionedOrderLineEntity>(entity => entity.HasKey(line => line.Id));
    }
}

/// <summary>Mapper for the versioned order aggregate.</summary>
[SparseFragmentMapper<VersionedOrderEntity, VersionedOrderDto>]
public sealed partial class VersionedOrderMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Name, entity => entity.CustomerName);
        // The token is read-only for the mapper: the context advances it,
        // while EF guards the conditional write with it.
        Map.Ignore(dto => dto.Version);
        Map.Read(dto => dto.Version, entity => entity.Version);
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Read(line => line.Id, entity => entity.Id);
                    child.Ignore(line => line.TemporaryId);
                    child.Ignore(line => line.Subtotal);
                    child.Read(line => line.Subtotal, entity => entity.Quantity * entity.UnitPrice);
                });
                lines.Create(dto => new VersionedOrderLineEntity
                {
                    Quantity = dto.Quantity,
                    UnitPrice = dto.UnitPrice,
                });
                lines.Delete();
                lines.Order(entity => entity.SortOrder);
            }
        );
    }
}
