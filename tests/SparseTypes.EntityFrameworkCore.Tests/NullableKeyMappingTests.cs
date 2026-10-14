using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

public partial class NkLineDto
{
    public int? Id { get; set; }

    [SparseKey(Unassigned = 0)]
    public int Key => Id ?? 0;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Qty { get; set; }

    public decimal UnitPrice { get; set; }
}

[SparseModel]
public partial class NkOrderDto
{
    public string Customer { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public List<NkLineDto> Lines { get; set; } = [];
}

public sealed class NkLineEntity
{
    public int Id { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Qty { get; set; }

    public decimal UnitPrice { get; set; }

    public int SortOrder { get; set; }

    public int OrderId { get; set; }

    public NkOrderEntity? Order { get; set; }
}

public sealed class NkOrderEntity
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public List<NkLineEntity> Lines { get; set; } = [];
}

public sealed class NkContext : DbContext
{
    public NkContext(DbContextOptions<NkContext> options)
        : base(options) { }

    public DbSet<NkOrderEntity> Orders => Set<NkOrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NkLineEntity>(entity => entity.HasKey(line => line.Id));
        modelBuilder.Entity<NkOrderEntity>(entity =>
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

[SparseFragmentMapper<NkOrderEntity, NkOrderDto>]
public sealed partial class NkMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Customer, entity => entity.CustomerName);
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(line => line.TemporaryId);
                });
                lines.Delete();
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );
    }
}

/// <summary>Nullable-identity keyed mapping backing the playground order shape.</summary>
public sealed class NullableKeyMappingTests
{
    [Test]
    public async Task NullableIdKeyShapeFlows()
    {
        using var connection = TestDatabases.OpenMemory();
        var options = new DbContextOptionsBuilder<NkContext>().UseSqlite(connection).Options;
        using var context = new NkContext(options);
        context.Database.EnsureCreated();
        var entity = context
            .Orders.Add(
                new NkOrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Lines =
                    [
                        new NkLineEntity
                        {
                            Product = "Espresso",
                            Qty = 2,
                            UnitPrice = 3.5m,
                            SortOrder = 0,
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();

        static NkOrderDto Read(NkOrderEntity order) =>
            new()
            {
                Customer = order.CustomerName,
                Notes = order.Notes,
                Lines = order
                    .Lines.OrderBy(static line => line.SortOrder)
                    .Select(static line => new NkLineDto
                    {
                        Id = line.Id,
                        Product = line.Product,
                        Qty = line.Qty,
                        UnitPrice = line.UnitPrice,
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines[0].Qty = 5;
        incoming.Lines.Add(
            new NkLineDto
            {
                Product = "Latte",
                Qty = 1,
                UnitPrice = 4.25m,
                TemporaryId = Guid.NewGuid(),
            }
        );
        var reordered = new List<NkLineDto> { incoming.Lines[1], incoming.Lines[0] };
        incoming.Lines.Clear();
        incoming.Lines.AddRange(reordered);
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<NkOrderEntity, NkOrderDto>
            .Create(new NkMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(2);
        entity.Lines.Select(static line => line.Qty).ShouldBe([1, 5]);
        entity.Lines.Select(static line => line.SortOrder).ShouldBe([0, 1]);
        entity.Lines.Single(static line => line.Qty == 1).UnitPrice.ShouldBe(4.25m);
    }
}
