using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>DTO holding lines keyed by a nullable store identity.</summary>
[SparseModel]
public partial class DefaultsOnlyDto
{
    /// <summary>Gets or sets the lines.</summary>
    public List<DefaultsOnlyLineDto> Lines { get; set; } = [];
}

/// <summary>Line DTO with a nullable identity like the playground order lines.</summary>
public partial class DefaultsOnlyLineDto
{
    /// <summary>Gets or sets the persisted identity. Null means unassigned.</summary>
    public int? Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id ?? 0;

    /// <summary>Gets or sets the collection-scoped temporary identity for new rows.</summary>
    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }
}

/// <summary>Collections relying on generated child defaults without an explicit Element mapping.</summary>
public sealed partial class CollectionDefaultsOnlyTests
{
    /// <summary>Entity holding the lines.</summary>
    public sealed class DefaultsOnlyEntity
    {
        /// <summary>Gets or sets the identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the lines.</summary>
        public List<DefaultsOnlyLineEntity> Lines { get; set; } = [];
    }

    /// <summary>Tracked line entity.</summary>
    public sealed class DefaultsOnlyLineEntity
    {
        /// <summary>Gets or sets the identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the quantity.</summary>
        public int Qty { get; set; }

        /// <summary>Gets or sets the persisted position.</summary>
        public int SortOrder { get; set; }

        /// <summary>Gets or sets the owning order identity.</summary>
        public int OrderId { get; set; }

        /// <summary>Gets or sets the owning order.</summary>
        public DefaultsOnlyEntity? Order { get; set; }
    }

    /// <summary>Ad-hoc context for the defaults-only fixture.</summary>
    public sealed class DefaultsOnlyContext(DbContextOptions<DefaultsOnlyContext> options)
        : DbContext(options)
    {
        /// <summary>Gets the orders.</summary>
        public DbSet<DefaultsOnlyEntity> Orders => Set<DefaultsOnlyEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DefaultsOnlyLineEntity>(entity => entity.HasKey(line => line.Id));
            modelBuilder.Entity<DefaultsOnlyEntity>(entity =>
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

    /// <summary>Mapping without Element: same-name Qty resolves via generated defaults.</summary>
    [SparseFragmentMapper<DefaultsOnlyEntity, DefaultsOnlyDto>]
    public sealed partial class DefaultsOnlyMapper
    {
        private void Configure()
        {
            Map.Collection<DefaultsOnlyLineDto, DefaultsOnlyLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Delete();
                    lines.Order((entity, index) => entity.SortOrder = index);
                }
            );
        }
    }

    private static DefaultsOnlyDto Read(DefaultsOnlyEntity entity) =>
        new()
        {
            Lines = entity
                .Lines.OrderBy(static line => line.SortOrder)
                .Select(static line => new DefaultsOnlyLineDto { Id = line.Id, Qty = line.Qty })
                .ToList(),
        };

    [Test]
    public async Task OmittedElementUsesGeneratedChildDefaults()
    {
        using var connection = TestDatabases.OpenMemory();
        var options = new DbContextOptionsBuilder<DefaultsOnlyContext>()
            .UseSqlite(connection)
            .Options;
        using var context = new DefaultsOnlyContext(options);
        await context.Database.EnsureCreatedAsync();
        var entity = context
            .Orders.Add(
                new DefaultsOnlyEntity
                {
                    Lines =
                    [
                        new DefaultsOnlyLineEntity { Qty = 1, SortOrder = 0 },
                        new DefaultsOnlyLineEntity { Qty = 2, SortOrder = 1 },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();

        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.Single(static line => line.Qty == 1).Qty = 10;
        incoming.Lines.RemoveAll(static line => line.Qty == 2);

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<DefaultsOnlyEntity, DefaultsOnlyDto>
            .Create(new DefaultsOnlyMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(1);
        entity.Lines.Single().Qty.ShouldBe(10);
    }
}
