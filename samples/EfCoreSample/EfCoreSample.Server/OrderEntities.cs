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

    /// <summary>
    /// Gets or sets the application-managed concurrency token. SQLite cannot
    /// store-generate a rowversion, so the context assigns a new value on
    /// every order or order-line write (see <see cref="SampleDbContext"/>).
    /// </summary>
    public Guid Version { get; set; }

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

    /// <summary>Saves changes after advancing aggregate concurrency tokens.</summary>
    /// <returns>The number of state entries written to the database.</returns>
    public override int SaveChanges()
    {
        AdvanceConcurrencyTokens();
        return base.SaveChanges();
    }

    /// <summary>Saves changes after advancing aggregate concurrency tokens.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AdvanceConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Assigns a new <see cref="OrderEntity.Version"/> to every added or modified
    /// order, and to the parent order of every added, modified, or deleted line.
    /// A child-only write leaves the parent row untouched by EF change tracking,
    /// so without this step two writers could pass the same token and the second
    /// update would silently overwrite the first.
    /// </summary>
    private void AdvanceConcurrencyTokens()
    {
        var parents = new HashSet<int>();
        foreach (var line in ChangeTracker.Entries<OrderLineEntity>().ToList())
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
                    .Entries<OrderEntity>()
                    .Select(static entry => entry.Entity)
                    .FirstOrDefault(order => order.Id == line.Entity.OrderId);
            if (parent is not null)
            {
                parents.Add(parent.Id);
            }
        }

        foreach (var entry in ChangeTracker.Entries<OrderEntity>().ToList())
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
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            // Application-managed token: the value is assigned in
            // AdvanceConcurrencyTokens, while EF only guards the write with it.
            entity.Property(order => order.Version).IsConcurrencyToken();
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
