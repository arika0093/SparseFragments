using Microsoft.EntityFrameworkCore;

namespace SparseFragments.EntityFrameworkCore.Tests;

public partial class ReqNoteLineDto
{
    public int? Id { get; set; }

    [SparseKey(Unassigned = 0)]
    public int Key => Id ?? 0;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Qty { get; set; }
}

[SparseFragmentModel]
public partial class ReqNoteOrderDto
{
    public string Customer { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public List<ReqNoteLineDto> Lines { get; set; } = [];
}

public sealed class ReqNoteEntity
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public ReqNoteOrderEntity? Order { get; set; }
}

public sealed class ReqNoteLineEntity
{
    public int Id { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Qty { get; set; }

    public int SortOrder { get; set; }

    public int OrderId { get; set; }

    public ReqNoteOrderEntity? Order { get; set; }
}

public sealed class ReqNoteOrderEntity
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public ReqNoteEntity? Note { get; set; }

    public List<ReqNoteLineEntity> Lines { get; set; } = [];
}

public sealed class ReqNoteContext : DbContext
{
    public ReqNoteContext(DbContextOptions<ReqNoteContext> options)
        : base(options) { }

    public DbSet<ReqNoteOrderEntity> Orders => Set<ReqNoteOrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReqNoteLineEntity>(entity => entity.HasKey(line => line.Id));
        modelBuilder.Entity<ReqNoteOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity
                .HasOne(order => order.Note)
                .WithOne(note => note.Order)
                .HasForeignKey<ReqNoteEntity>(note => note.OrderId)
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

public sealed class ReqNoteMapper : IEntityModelMapping<ReqNoteOrderEntity, ReqNoteOrderDto>
{
    public void Configure(SparseEntityMapBuilder<ReqNoteOrderEntity, ReqNoteOrderDto> map)
    {
        map.Member(dto => dto.Customer, entity => entity.CustomerName);
        map.Member(dto => dto.Notes, entity => entity.Note!.Text);
        map.Collection<ReqNoteLineDto, ReqNoteLineEntity>(
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

/// <summary>Flattened writes into a required 1:1 dependent backing the playground note shape.</summary>
public sealed class FlattenedRequiredRelationTests
{
    [Test]
    public async Task FlattenedRequiredOneToOneFlows()
    {
        using var connection = TestDatabases.OpenMemory();
        var options = new DbContextOptionsBuilder<ReqNoteContext>().UseSqlite(connection).Options;
        using var context = new ReqNoteContext(options);
        context.Database.EnsureCreated();
        var entity = context
            .Orders.Add(
                new ReqNoteOrderEntity
                {
                    CustomerName = "Ann",
                    Total = 1m,
                    Note = new ReqNoteEntity { Text = "keep" },
                    Lines =
                    [
                        new ReqNoteLineEntity
                        {
                            Product = "Espresso",
                            Qty = 2,
                            SortOrder = 0,
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        await context.Entry(entity).Reference(static order => order.Note).LoadAsync();

        static ReqNoteOrderDto Read(ReqNoteOrderEntity order) =>
            new()
            {
                Customer = order.CustomerName,
                Notes = order.Note?.Text ?? string.Empty,
                Total = order.Total,
                Lines = order
                    .Lines.OrderBy(static line => line.SortOrder)
                    .Select(static line => new ReqNoteLineDto
                    {
                        Id = line.Id,
                        Product = line.Product,
                        Qty = line.Qty,
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Notes = "updated";
        incoming.Total = 2m;
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<ReqNoteOrderEntity, ReqNoteOrderDto>
            .Create(new ReqNoteMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Note!.Text.ShouldBe("updated");
        entity.Total.ShouldBe(2m);
        context.Entry(entity.Note).Property(static note => note.Text).IsModified.ShouldBeTrue();

        await context.SaveChangesAsync();
        using var verify = new ReqNoteContext(
            new DbContextOptionsBuilder<ReqNoteContext>().UseSqlite(connection).Options
        );
        verify.Orders.Include(static order => order.Note).Single().Note!.Text.ShouldBe("updated");
    }
}
