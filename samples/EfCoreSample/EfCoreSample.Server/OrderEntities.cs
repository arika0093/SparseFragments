using Microsoft.EntityFrameworkCore;

namespace EfCoreSample.Server;

/// <summary>
/// The owned address entity. Carries no mapping attributes.
/// </summary>
public sealed class AddressEntity
{
    /// <summary>Gets or sets the street.</summary>
    public string Street { get; set; } = string.Empty;

    /// <summary>Gets or sets the city.</summary>
    public string City { get; set; } = string.Empty;
}

/// <summary>
/// The tracked order entity. Carries no mapping attributes.
/// </summary>
public sealed class OrderEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the customer name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Gets or sets the normalized code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Gets or sets the owned address.</summary>
    public AddressEntity? Address { get; set; }

    /// <summary>Gets or sets the keyed order lines.</summary>
    public List<OrderLineEntity> Lines { get; set; } = [];
}

/// <summary>
/// The tracked order line entity. Carries no mapping attributes.
/// </summary>
public sealed class OrderLineEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the owning order identity.</summary>
    public int OrderId { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the persisted position.</summary>
    public int SortOrder { get; set; }

    /// <summary>Gets or sets the owning order.</summary>
    public OrderEntity? Order { get; set; }
}

/// <summary>
/// The sample document context.
/// </summary>
public sealed class SampleDbContext : DbContext
{
    /// <summary>Creates a sample context.</summary>
    /// <param name="options">The context options.</param>
    public SampleDbContext(DbContextOptions<SampleDbContext> options)
        : base(options) { }

    /// <summary>Gets the orders.</summary>
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            // Provider-neutral concurrency token (IsRowVersion is SQL
            // Server-oriented; SQLite cannot store-generate it).
            entity.Property(order => order.RowVersion).IsConcurrencyToken();
            entity.OwnsOne(order => order.Address);
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OrderLineEntity>(entity =>
        {
            entity.HasKey(line => line.Id);
        });
    }
}
