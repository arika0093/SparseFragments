using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SparseFragments;
using SparseFragments.EntityFrameworkCore;

namespace SparseFragments.Playground.Models;

/// <summary>Order DTO edited in playground section 05. Knows nothing about EF Core.</summary>
[SparseFragmentModel]
public partial class PlaygroundOrderDto
{
    /// <summary>Gets or sets the customer. Renamed from the entity member.</summary>
    public string Customer { get; set; } = string.Empty;

    /// <summary>Gets or sets the notes. Same-name scalar mapping.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Gets or sets the keyed order lines.</summary>
    public List<PlaygroundOrderLineDto> Lines { get; set; } = [];
}

/// <summary>Order line DTO keyed by its editable identity.</summary>
public partial class PlaygroundOrderLineDto
{
    /// <summary>Gets or sets the persisted identity. Null means unassigned.</summary>
    public int? Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id ?? 0;

    /// <summary>Gets or sets the collection-scoped temporary identity for new rows.</summary>
    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    /// <summary>Gets or sets the stock-keeping unit.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Gets or sets the product name.</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>Tracked order entity. Carries no mapping attributes.</summary>
public sealed class PlaygroundOrderEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the customer name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Gets or sets the keyed order lines.</summary>
    public List<PlaygroundOrderLineEntity> Lines { get; set; } = [];
}

/// <summary>Tracked order line entity. Carries no mapping attributes.</summary>
public sealed class PlaygroundOrderLineEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the stock-keeping unit.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Gets or sets the product name.</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the persisted position.</summary>
    public int SortOrder { get; set; }

    /// <summary>Gets or sets the owning order identity.</summary>
    public int OrderId { get; set; }

    /// <summary>Gets or sets the owning order.</summary>
    public PlaygroundOrderEntity? Order { get; set; }
}

/// <summary>In-memory document context for playground section 05.</summary>
public sealed class PlaygroundEfContext : DbContext
{
    /// <summary>Creates a playground context.</summary>
    /// <param name="options">The context options.</param>
    public PlaygroundEfContext(DbContextOptions<PlaygroundEfContext> options)
        : base(options) { }

    /// <summary>Gets the orders.</summary>
    public DbSet<PlaygroundOrderEntity> Orders => Set<PlaygroundOrderEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlaygroundOrderLineEntity>(entity =>
        {
            entity.HasKey(line => line.Id);
            entity.HasAlternateKey(line => line.Sku);
        });
        modelBuilder.Entity<PlaygroundOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Maps order DTO changes onto the tracked order entity.</summary>
[SparseFragmentMapper]
public sealed class PlaygroundOrderMapper
    : IEntityModelMapping<PlaygroundOrderEntity, PlaygroundOrderDto>
{
    /// <inheritdoc />
    /// <remarks>
    /// The <c>typeof</c> reference roots the source-generated companion for
    /// trimmed builds; the generator emits it into this same compilation.
    /// </remarks>
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.All,
        typeof(PlaygroundOrderMapper_SparseEfDefaults)
    )]
    public void Configure(SparseEntityMapBuilder<PlaygroundOrderEntity, PlaygroundOrderDto> map)
    {
        map.Member(dto => dto.Customer, entity => entity.CustomerName);
        map.Collection<PlaygroundOrderLineDto, PlaygroundOrderLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(line => line.Id);
                    child.Ignore(line => line.TemporaryId);
                });
                lines.Delete();
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );
    }
}
