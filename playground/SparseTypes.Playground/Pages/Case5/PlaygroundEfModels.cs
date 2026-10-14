using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using SparseTypes;
using SparseTypes.EntityFrameworkCore;

namespace SparseTypes.Playground.Models;

/// <summary>Order DTO for Case 5. Knows nothing about EF Core.</summary>
[SparseModel]
public partial class PlaygroundOrderDto
{
    /// <summary>Gets or sets the customer. Renamed from the entity member.</summary>
    public string Customer { get; set; } = string.Empty;

    /// <summary>Gets or sets the notes. Same-name scalar mapping.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Gets or sets the order total. Same-name scalar mapping.</summary>
    public decimal Total { get; set; }

    /// <summary>Gets or sets the keyed order lines.</summary>
    public List<PlaygroundOrderLineDto> Lines { get; set; } = [];
}

/// <summary>Order line DTO keyed by its store-assigned identity.</summary>
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

    /// <summary>Gets or sets the product name.</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }
}

/// <summary>Separate relational note entity (1:1 with the order).</summary>
public sealed class PlaygroundNoteEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the note text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the owning order identity.</summary>
    public int OrderId { get; set; }

    /// <summary>Gets or sets the owning order.</summary>
    public PlaygroundOrderEntity? Order { get; set; }
}

/// <summary>Tracked order entity. Carries no mapping attributes.</summary>
public sealed class PlaygroundOrderEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the optimistic concurrency revision.</summary>
    public int Revision { get; set; }

    /// <summary>Gets or sets the customer name.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the order total.</summary>
    public decimal Total { get; set; }

    /// <summary>Gets or sets the separate note.</summary>
    public PlaygroundNoteEntity? Note { get; set; }

    /// <summary>Gets or sets the keyed order lines.</summary>
    public List<PlaygroundOrderLineEntity> Lines { get; set; } = [];
}

/// <summary>Tracked order line entity. Carries no mapping attributes.</summary>
public sealed class PlaygroundOrderLineEntity
{
    /// <summary>Gets or sets the database-generated identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the product name.</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

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
        // The stable key is Id, so no alternate keys are declared: they would
        // protect those writes and reject the generated same-name defaults
        // at mapper creation.
        modelBuilder.Entity<PlaygroundOrderLineEntity>(entity =>
        {
            entity.HasKey(line => line.Id);
        });
        modelBuilder.Entity<PlaygroundOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Revision).IsConcurrencyToken();
            entity
                .HasOne(order => order.Note)
                .WithOne(note => note.Order)
                .HasForeignKey<PlaygroundNoteEntity>(note => note.OrderId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Maps order DTO changes onto the tracked order entity.</summary>
[SparseFragmentMapper<PlaygroundOrderEntity, PlaygroundOrderDto>]
public sealed partial class PlaygroundOrderMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Customer, entity => entity.CustomerName);
        Map.Member(dto => dto.Notes, entity => entity.Note!.Text);
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                // Same-name Product/Qty resolve via generated defaults.
                lines.Delete();
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );
    }
}
