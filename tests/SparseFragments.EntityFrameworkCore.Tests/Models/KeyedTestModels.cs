using EfCoreSample.Server;
using Microsoft.EntityFrameworkCore;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>DTO line with a computed composite key.</summary>
public partial class CompositeLineDto
{
    /// <summary>Gets or sets the tenant.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the line number.</summary>
    public int LineNo { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets the stable composite key.</summary>
    [SparseKey]
    public (string TenantId, int LineNo) Key => (TenantId, LineNo);
}

/// <summary>DTO holding composite-keyed lines.</summary>
[SparseFragmentModel]
public partial class CompositeOrderDto
{
    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the lines.</summary>
    public List<CompositeLineDto> Lines { get; set; } = [];
}

/// <summary>Entity holding composite-keyed lines.</summary>
public sealed class CompositeOrderEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the lines.</summary>
    public List<CompositeLineEntity> Lines { get; set; } = [];
}

/// <summary>Line entity with a composite primary key.</summary>
public sealed class CompositeLineEntity
{
    /// <summary>Gets or sets the tenant.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the line number.</summary>
    public int LineNo { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the owning order identity.</summary>
    public int OrderId { get; set; }

    /// <summary>Gets or sets the owning order.</summary>
    public CompositeOrderEntity? Order { get; set; }
}

/// <summary>Composite-keyed order mapping with an explicit DTO to EF key projection.</summary>
public sealed class CompositeOrderMapper
    : IEntityModelMapping<CompositeOrderEntity, CompositeOrderDto>
{
    /// <inheritdoc />
    public void Configure(SparseEntityMapBuilder<CompositeOrderEntity, CompositeOrderDto> map)
    {
        map.Collection<CompositeLineDto, CompositeLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.AllowWrite(line => line.TenantId);
                    child.AllowWrite(line => line.LineNo);
                });
                lines.Key(
                    dto => dto.Key,
                    entity => ValueTuple.Create(entity.TenantId, entity.LineNo)
                );
                lines.Delete();
            }
        );
    }
}

/// <summary>DTO line keyed by a natural alternate identity.</summary>
public partial class SkuLineDto
{
    /// <summary>Gets or sets the stock-keeping unit.</summary>
    [SparseKey]
    public string Sku { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }
}

/// <summary>DTO holding naturally keyed lines.</summary>
[SparseFragmentModel]
public partial class SkuOrderDto
{
    /// <summary>Gets or sets the lines.</summary>
    public List<SkuLineDto> Lines { get; set; } = [];
}

/// <summary>Entity holding naturally keyed lines.</summary>
public sealed class SkuOrderEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the lines.</summary>
    public List<SkuLineEntity> Lines { get; set; } = [];
}

/// <summary>Line entity with a surrogate primary key beside the natural alternate key.</summary>
public sealed class SkuLineEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the stock-keeping unit.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the owning order identity.</summary>
    public int OrderId { get; set; }

    /// <summary>Gets or sets the owning order.</summary>
    public SkuOrderEntity? Order { get; set; }
}

/// <summary>Natural-key order mapping: the DTO key meets the EF alternate key, not the primary key.</summary>
public sealed class SkuOrderMapper : IEntityModelMapping<SkuOrderEntity, SkuOrderDto>
{
    /// <inheritdoc />
    public void Configure(SparseEntityMapBuilder<SkuOrderEntity, SkuOrderDto> map)
    {
        map.Collection<SkuLineDto, SkuLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.AllowWrite(line => line.Sku);
                });
                lines.Key(dto => dto.Sku, entity => entity.Sku);
                lines.Delete();
            }
        );
    }
}

/// <summary>DTO line shared by two parent collections.</summary>
public partial class DuoLineDto
{
    /// <summary>Gets or sets the persisted identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    /// <summary>Gets or sets the collection-scoped temporary identity.</summary>
    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the note.</summary>
    public string? Note { get; set; }
}

/// <summary>DTO holding two collections of the same element type pair.</summary>
[SparseFragmentModel]
public partial class DuoDto
{
    /// <summary>Gets or sets the active lines.</summary>
    public List<DuoLineDto> Actives { get; set; } = [];

    /// <summary>Gets or sets the archived lines.</summary>
    public List<DuoLineDto> Archived { get; set; } = [];
}

/// <summary>Entity holding two collections of the same element type.</summary>
public sealed class DuoEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the active lines.</summary>
    public List<DuoLineEntity> Actives { get; set; } = [];

    /// <summary>Gets or sets the archived lines.</summary>
    public List<DuoLineEntity> Archived { get; set; } = [];
}

/// <summary>Line entity linked to either parent collection through an optional foreign key.</summary>
public sealed class DuoLineEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the note.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets the active owner identity.</summary>
    public int? ActiveOwnerId { get; set; }

    /// <summary>Gets or sets the active owner.</summary>
    public DuoEntity? ActiveOwner { get; set; }

    /// <summary>Gets or sets the archived owner identity.</summary>
    public int? ArchivedOwnerId { get; set; }

    /// <summary>Gets or sets the archived owner.</summary>
    public DuoEntity? ArchivedOwner { get; set; }
}

/// <summary>DTO line for the soft-delete collection.</summary>
public partial class SoftLineDto
{
    /// <summary>Gets or sets the persisted identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }
}

/// <summary>DTO holding soft-deleted lines.</summary>
[SparseFragmentModel]
public partial class SoftOrderDto
{
    /// <summary>Gets or sets the lines.</summary>
    public List<SoftLineDto> Lines { get; set; } = [];
}

/// <summary>Entity holding soft-deleted lines.</summary>
public sealed class SoftOrderEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the lines.</summary>
    public List<SoftLineEntity> Lines { get; set; } = [];
}

/// <summary>Line entity with a soft-delete flag under a required foreign key.</summary>
public sealed class SoftLineEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets whether the row is soft-deleted.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Gets or sets the owning order identity.</summary>
    public int OrderId { get; set; }

    /// <summary>Gets or sets the owning order.</summary>
    public SoftOrderEntity? Order { get; set; }
}

/// <summary>DTO line keyed by the surrogate identity under an optional composite FK.</summary>
public partial class BatchLineDto
{
    /// <summary>Gets or sets the persisted identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    /// <summary>Gets or sets the collection-scoped temporary identity.</summary>
    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }
}

/// <summary>DTO holding lines under an optional composite foreign key.</summary>
[SparseFragmentModel]
public partial class BatchDto
{
    /// <summary>Gets or sets the lines.</summary>
    public List<BatchLineDto> Lines { get; set; } = [];
}

/// <summary>Principal with a composite primary key.</summary>
public sealed class BatchEntity
{
    /// <summary>Gets or sets the tenant.</summary>
    public string Tenant { get; set; } = string.Empty;

    /// <summary>Gets or sets the batch number.</summary>
    public int BatchNo { get; set; }

    /// <summary>Gets or sets the lines.</summary>
    public List<BatchLineEntity> Lines { get; set; } = [];
}

/// <summary>Dependent with a mixed-nullability composite foreign key.</summary>
/// <remarks>
/// <c>Tenant</c> is nullable while <c>BatchNo</c> is not, and the relationship
/// is explicitly optional. Per-property nullability inference would misread
/// this as required; only <c>IForeignKey.IsRequired</c> is authoritative.
/// </remarks>
public sealed class BatchLineEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the tenant leg (nullable).</summary>
    public string? Tenant { get; set; }

    /// <summary>Gets or sets the batch-number leg (non-nullable).</summary>
    public int BatchNo { get; set; }

    /// <summary>Gets or sets the owning batch.</summary>
    public BatchEntity? Batch { get; set; }
}

/// <summary>DTO course reference for the skip-navigation probe.</summary>
public partial class CourseRefDto
{
    /// <summary>Gets or sets the persisted identity.</summary>
    [SparseKey]
    public int Id { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>DTO holding a many-to-many enrollment.</summary>
[SparseFragmentModel]
public partial class StudentDto
{
    /// <summary>Gets or sets the enrolled courses.</summary>
    public List<CourseRefDto> Courses { get; set; } = [];
}

/// <summary>Student side of a many-to-many skip navigation.</summary>
public sealed class StudentEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the enrolled courses.</summary>
    public List<CourseEntity> Courses { get; set; } = [];
}

/// <summary>Course side of a many-to-many skip navigation.</summary>
public sealed class CourseEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the enrolled students.</summary>
    public List<StudentEntity> Students { get; set; } = [];
}

/// <summary>Nested address DTO carried by a keyed element.</summary>
[SparseFragmentModel]
public partial class ParcelAddressDto
{
    /// <summary>Gets or sets the city.</summary>
    public string City { get; set; } = string.Empty;
}

/// <summary>Tag DTO nested one more level under a keyed element.</summary>
public partial class ParcelTagDto
{
    /// <summary>Gets or sets the tag label, acting as its stable key.</summary>
    [SparseKey]
    public string Label { get; set; } = string.Empty;
}

/// <summary>DTO line with a nested reference under keyed synchronization.</summary>
public partial class ParcelLineDto
{
    /// <summary>Gets or sets the persisted identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    /// <summary>Gets or sets the collection-scoped temporary identity.</summary>
    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the nested ship-to address.</summary>
    public ParcelAddressDto? ShipTo { get; set; }

    /// <summary>Gets or sets the nested tags.</summary>
    public List<ParcelTagDto> Tags { get; set; } = [];
}

/// <summary>DTO holding parcel lines.</summary>
[SparseFragmentModel]
public partial class ParcelDto
{
    /// <summary>Gets or sets the lines.</summary>
    public List<ParcelLineDto> Lines { get; set; } = [];
}

/// <summary>Entity holding parcel lines.</summary>
public sealed class ParcelEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the lines.</summary>
    public List<ParcelLineEntity> Lines { get; set; } = [];
}

/// <summary>Owned ship-to address of a parcel line.</summary>
public sealed class ParcelAddressEntity
{
    /// <summary>Gets or sets the city.</summary>
    public string City { get; set; } = string.Empty;
}

/// <summary>Tag entity nested under a parcel line.</summary>
public sealed class ParcelTagEntity
{
    /// <summary>Gets or sets the tag label, acting as its stable key.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the owning line identity.</summary>
    public int LineId { get; set; }

    /// <summary>Gets or sets the owning line.</summary>
    public ParcelLineEntity? Line { get; set; }
}

/// <summary>Line entity with a nested owned reference.</summary>
public sealed class ParcelLineEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the nested ship-to address.</summary>
    public ParcelAddressEntity? ShipTo { get; set; }

    /// <summary>Gets or sets the nested tags.</summary>
    public List<ParcelTagEntity> Tags { get; set; } = [];

    /// <summary>Gets or sets the owning parcel identity.</summary>
    public int ParcelId { get; set; }

    /// <summary>Gets or sets the owning parcel.</summary>
    public ParcelEntity? Parcel { get; set; }
}

/// <summary>SQLite in-memory context for the keyed mapping test models.</summary>
public sealed class KeyedMappingContext : DbContext
{
    /// <summary>Creates a keyed mapping context.</summary>
    /// <param name="options">The context options.</param>
    public KeyedMappingContext(DbContextOptions<KeyedMappingContext> options)
        : base(options) { }

    /// <summary>Gets the composite orders.</summary>
    public DbSet<CompositeOrderEntity> CompositeOrders => Set<CompositeOrderEntity>();

    /// <summary>Gets the natural-key orders.</summary>
    public DbSet<SkuOrderEntity> SkuOrders => Set<SkuOrderEntity>();

    /// <summary>Gets the two-collection owners.</summary>
    public DbSet<DuoEntity> Duos => Set<DuoEntity>();

    /// <summary>Gets the soft-delete owners.</summary>
    public DbSet<SoftOrderEntity> SoftOrders => Set<SoftOrderEntity>();

    /// <summary>Gets the composite-key batch owners.</summary>
    public DbSet<BatchEntity> Batches => Set<BatchEntity>();

    /// <summary>Gets the students.</summary>
    public DbSet<StudentEntity> Students => Set<StudentEntity>();

    /// <summary>Gets the courses.</summary>
    public DbSet<CourseEntity> Courses => Set<CourseEntity>();

    /// <summary>Gets the parcel owners.</summary>
    public DbSet<ParcelEntity> Parcels => Set<ParcelEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompositeLineEntity>(entity =>
        {
            entity.HasKey(line => new { line.TenantId, line.LineNo });
        });
        modelBuilder.Entity<CompositeOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SkuLineEntity>(entity =>
        {
            entity.HasKey(line => line.Id);
            entity.HasAlternateKey(line => line.Sku);
        });
        modelBuilder.Entity<SkuOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DuoEntity>(entity =>
        {
            entity.HasKey(owner => owner.Id);
            entity
                .HasMany(owner => owner.Actives)
                .WithOne(line => line.ActiveOwner)
                .HasForeignKey(line => line.ActiveOwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull);
            entity
                .HasMany(owner => owner.Archived)
                .WithOne(line => line.ArchivedOwner)
                .HasForeignKey(line => line.ArchivedOwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });
        modelBuilder.Entity<SoftOrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity
                .HasMany(order => order.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<BatchEntity>(entity =>
        {
            entity.HasKey(batch => new { batch.Tenant, batch.BatchNo });
            entity
                .HasMany(batch => batch.Lines)
                .WithOne(line => line.Batch)
                .HasForeignKey(line => new { line.Tenant, line.BatchNo })
                .IsRequired(false)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });
        modelBuilder.Entity<StudentEntity>(entity =>
        {
            entity.HasKey(student => student.Id);
            entity.HasMany(student => student.Courses).WithMany(course => course.Students);
        });
        modelBuilder.Entity<CourseEntity>(entity =>
        {
            entity.HasKey(course => course.Id);
        });
        modelBuilder.Entity<ParcelEntity>(entity =>
        {
            entity.HasKey(parcel => parcel.Id);
            entity
                .HasMany(parcel => parcel.Lines)
                .WithOne(line => line.Parcel)
                .HasForeignKey(line => line.ParcelId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ParcelLineEntity>(entity =>
        {
            entity.HasKey(line => line.Id);
            entity.OwnsOne(line => line.ShipTo);
            entity
                .HasMany(line => line.Tags)
                .WithOne(tag => tag.Line)
                .HasForeignKey(tag => tag.LineId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ParcelTagEntity>(entity =>
        {
            entity.HasKey(tag => tag.Label);
        });
    }
}
