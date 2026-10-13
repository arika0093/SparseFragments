using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseFragments;
using SparseFragments.EntityFrameworkCore;

[SparseFragmentModel]
public partial class EfApplyAddressDto
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class EfApplyLineDto
{
    public int Id { get; set; }

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

[SparseFragmentModel]
public partial class EfApplyOrderDto
{
    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string Code { get; set; } = string.Empty;

    public EfApplyAddressDto? Address { get; set; }

    public List<EfApplyLineDto> Lines { get; set; } = [];
}

public sealed class EfApplyAddressEntity
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}

public sealed class EfApplyLineEntity
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int SortOrder { get; set; }

    public EfApplyOrderEntity? Order { get; set; }
}

public sealed class EfApplyOrderEntity
{
    public int Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string Code { get; set; } = string.Empty;

    public EfApplyAddressEntity? ShippingAddress { get; set; }

    public List<EfApplyLineEntity> Lines { get; set; } = [];
}

public sealed class EfApplyDbContext(DbContextOptions<EfApplyDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfApplyOrderEntity>(order =>
        {
            order.HasKey(entity => entity.Id);
            order.OwnsOne(entity => entity.ShippingAddress);
            order
                .HasMany(entity => entity.Lines)
                .WithOne(line => line.Order)
                .HasForeignKey(line => line.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<EfApplyLineEntity>().HasKey(line => line.Id);
    }
}

[SparseFragmentMapper]
public sealed class EfApplyOrderMapper : IEntityModelMapping<EfApplyOrderEntity, EfApplyOrderDto>
{
    public void Configure(SparseEntityMapBuilder<EfApplyOrderEntity, EfApplyOrderDto> map)
    {
        map.Member(dto => dto.Name, entity => entity.CustomerName);
        map.Member(dto => dto.Notes, entity => entity.Notes);
        map.Member(dto => dto.Code, entity => entity.Code);
        map.Object(
            dto => dto.Address,
            entity => entity.ShippingAddress,
            address =>
            {
                address.Member(dto => dto.Street, entity => entity.Street);
                address.Member(dto => dto.City, entity => entity.City);
            }
        );
        map.Collection<EfApplyLineDto, EfApplyLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(dto => dto.Id);
                    child.Ignore(dto => dto.TemporaryId);
                    child.Member(dto => dto.Sku, entity => entity.Sku);
                    child.Member(dto => dto.Quantity, entity => entity.Quantity);
                });
                lines.Key(dto => dto.Key, entity => entity.Id);
                lines.Create(dto => new EfApplyLineEntity
                {
                    Sku = dto.Sku,
                    Quantity = dto.Quantity,
                });
                lines.Delete();
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );
    }
}

internal sealed class EfCoreApplyBenchmarkFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EfApplyDbContext> _options;
    private readonly EfApplyDbContext _modelContext;

    public EfCoreApplyBenchmarkFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EfApplyDbContext>().UseSqlite(_connection).Options;
        _modelContext = new EfApplyDbContext(_options);
        _modelContext.Database.EnsureCreated();
        Mapper = SparseEntityMapper<EfApplyOrderEntity, EfApplyOrderDto>.Create(
            new EfApplyOrderMapper(),
            _modelContext.Model
        );
    }

    public SparseEntityMapper<EfApplyOrderEntity, EfApplyOrderDto> Mapper { get; }

    public (EfApplyDbContext Context, EfApplyOrderEntity Entity) CreateTrackedOrder(int lineCount)
    {
        var entity = new EfApplyOrderEntity
        {
            Id = 1,
            CustomerName = "Ada Lovelace",
            Notes = "Benchmark order",
            Code = "ORDER-001",
            ShippingAddress = new EfApplyAddressEntity
            {
                Street = "1 Analytical Engine Way",
                City = "London",
            },
            Lines = Enumerable
                .Range(0, lineCount)
                .Select(index => new EfApplyLineEntity
                {
                    Id = index + 1,
                    OrderId = 1,
                    Sku = "SKU-" + index,
                    Quantity = index + 1,
                    SortOrder = index,
                })
                .ToList(),
        };
        var context = new EfApplyDbContext(_options);
        context.Attach(entity);
        context.Entry(entity).Collection(order => order.Lines).IsLoaded = true;
        return (context, entity);
    }

    public static EfApplyOrderDto ToDto(EfApplyOrderEntity entity) =>
        new()
        {
            Name = entity.CustomerName,
            Notes = entity.Notes,
            Code = entity.Code,
            Address = entity.ShippingAddress is null
                ? null
                : new EfApplyAddressDto
                {
                    Street = entity.ShippingAddress.Street,
                    City = entity.ShippingAddress.City,
                },
            Lines = entity
                .Lines.OrderBy(static line => line.SortOrder)
                .Select(static line => new EfApplyLineDto
                {
                    Id = line.Id,
                    Sku = line.Sku,
                    Quantity = line.Quantity,
                })
                .ToList(),
        };

    public void Dispose()
    {
        _modelContext.Dispose();
        _connection.Dispose();
    }
}

[MemoryDiagnoser]
public class EfCoreScalarApplyBenchmarks
{
    private EfCoreApplyBenchmarkFixture _fixture = null!;
    private EfApplyDbContext _context = null!;
    private EfApplyOrderEntity _entity = null!;
    private EfApplyOrderDto.ChangeSet _changes = null!;

    [GlobalSetup]
    public void Setup() => _fixture = new EfCoreApplyBenchmarkFixture();

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    [IterationSetup]
    public void SetupInvocation()
    {
        (_context, _entity) = _fixture.CreateTrackedOrder(16);
        var before = EfCoreApplyBenchmarkFixture.ToDto(_entity);
        var after = EfCoreApplyBenchmarkFixture.ToDto(_entity);
        after.Name = "Grace Hopper";
        after.Notes = "Revised benchmark order";
        after.Code = "ORDER-002";
        _changes = before.CreateChangeSet(after);
    }

    [IterationCleanup]
    public void CleanupInvocation() => _context.Dispose();

    [Benchmark]
    public void ApplyScalarEdits() => _fixture.Mapper.Apply(_context, _entity, _changes);

    [Benchmark]
    public SparseEntityApplyResult ApplyWithResultScalarEdits() =>
        _fixture.Mapper.ApplyWithResult(_context, _entity, _changes);
}

[MemoryDiagnoser]
public class EfCoreNestedApplyBenchmarks
{
    private EfCoreApplyBenchmarkFixture _fixture = null!;
    private EfApplyDbContext _context = null!;
    private EfApplyOrderEntity _entity = null!;
    private EfApplyOrderDto.ChangeSet _changes = null!;

    [GlobalSetup]
    public void Setup() => _fixture = new EfCoreApplyBenchmarkFixture();

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    [IterationSetup]
    public void SetupInvocation()
    {
        (_context, _entity) = _fixture.CreateTrackedOrder(16);
        var before = EfCoreApplyBenchmarkFixture.ToDto(_entity);
        var after = EfCoreApplyBenchmarkFixture.ToDto(_entity);
        var afterAddress = after.Address!;
        afterAddress.Street = "2 Benchmark Square";
        afterAddress.City = "Cambridge";
        _changes = before.CreateChangeSet(after);
    }

    [IterationCleanup]
    public void CleanupInvocation() => _context.Dispose();

    [Benchmark]
    public void ApplyNestedEdits() => _fixture.Mapper.Apply(_context, _entity, _changes);

    [Benchmark]
    public SparseEntityApplyResult ApplyWithResultNestedEdits() =>
        _fixture.Mapper.ApplyWithResult(_context, _entity, _changes);
}

public enum EfCoreKeyedMutation
{
    Add,
    Edit,
    Remove,
    Reorder,
}

[MemoryDiagnoser]
public class EfCoreKeyedCollectionApplyBenchmarks
{
    private EfCoreApplyBenchmarkFixture _fixture = null!;
    private EfApplyDbContext _context = null!;
    private EfApplyOrderEntity _entity = null!;
    private EfApplyOrderDto.ChangeSet _changes = null!;

    [Params(16, 128)]
    public int CollectionSize { get; set; }

    [ParamsAllValues]
    public EfCoreKeyedMutation Mutation { get; set; }

    [GlobalSetup]
    public void Setup() => _fixture = new EfCoreApplyBenchmarkFixture();

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    [IterationSetup]
    public void SetupInvocation()
    {
        (_context, _entity) = _fixture.CreateTrackedOrder(CollectionSize);
        var before = EfCoreApplyBenchmarkFixture.ToDto(_entity);
        var after = EfCoreApplyBenchmarkFixture.ToDto(_entity);
        var middle = CollectionSize / 2;
        switch (Mutation)
        {
            case EfCoreKeyedMutation.Add:
                after.Lines.Add(
                    new EfApplyLineDto
                    {
                        Sku = "SKU-NEW",
                        Quantity = CollectionSize + 1,
                        TemporaryId = Guid.NewGuid(),
                    }
                );
                break;
            case EfCoreKeyedMutation.Edit:
                after.Lines[middle].Quantity++;
                break;
            case EfCoreKeyedMutation.Remove:
                after.Lines.RemoveAt(middle);
                break;
            case EfCoreKeyedMutation.Reorder:
                after.Lines.Reverse();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Mutation));
        }
        _changes = before.CreateChangeSet(after);
    }

    [IterationCleanup]
    public void CleanupInvocation() => _context.Dispose();

    [Benchmark]
    public void ApplyKeyedCollectionChanges() => _fixture.Mapper.Apply(_context, _entity, _changes);

    [Benchmark]
    public SparseEntityApplyResult ApplyKeyedCollectionChangesWithResult() =>
        _fixture.Mapper.ApplyWithResult(_context, _entity, _changes);
}
