using Microsoft.EntityFrameworkCore;

namespace SparseFragments.EntityFrameworkCore.Tests.Sample;

[SparseFragmentModel]
public partial class AddressDto
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class OrderDto
{
    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string Code { get; set; } = string.Empty;

    public string ShipCity { get; set; } = string.Empty;

    public string? DisplayTotal { get; set; }

    public AddressDto? Address { get; set; }

    public List<OrderLineDto> Lines { get; set; } = [];
}

public partial class OrderLineDto
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

public sealed class AddressEntity
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}

public sealed class OrderEntity
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string Code { get; set; } = string.Empty;

    public byte[] RowVersion { get; set; } = [];

    public AddressEntity? Address { get; set; }

    public List<OrderLineEntity> Lines { get; set; } = [];
}

public sealed class OrderLineEntity
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public int SortOrder { get; set; }

    public OrderEntity? Order { get; set; }
}

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.RowVersion).IsConcurrencyToken();
            entity.OwnsOne(order => order.Address);
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OrderLineEntity>(entity => entity.HasKey(line => line.Id));
    }
}

[SparseFragmentMapper]
public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
{
    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
    {
        map.Member(dto => dto.Name, entity => entity.CustomerName);
        map.Member(
            dto => dto.Code,
            entity => entity.Code,
            code => code == null ? null : code.ToUpperInvariant()
        );
        map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        map.Ignore(dto => dto.DisplayTotal);
        map.Object(
            dto => dto.Address,
            entity => entity.Address,
            child => child.Ignore(address => address.City)
        );
        map.Collection<OrderLineDto, OrderLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(line => line.Id);
                    child.Ignore(line => line.TemporaryId);
                    child.Ignore(line => line.Subtotal);
                });
                lines.Create(dto => new OrderLineEntity
                {
                    Quantity = dto.Quantity,
                    UnitPrice = dto.UnitPrice,
                });
                lines.Delete();
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );
    }
}
