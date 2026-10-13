using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Verifies projections built from entity mapping registrations.</summary>
public sealed partial class ReadProjectionTests
{
    [Test]
    public async Task ProjectionTranslatesAndProjectsNestedOrderedCollections()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Code = "stored",
                    Address = new AddressEntity { Street = "1 Main", City = "Osaka" },
                    Lines =
                    [
                        new OrderLineEntity
                        {
                            Quantity = 2,
                            UnitPrice = 3m,
                            SortOrder = 2,
                        },
                        new OrderLineEntity
                        {
                            Quantity = 1,
                            UnitPrice = 4m,
                            SortOrder = 1,
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new ProjectionOrderMapper(),
            context.Model
        );
        counter.Reset();

        var query = context.Orders.Where(order => order.Id == entity.Id).Select(mapper.Projection);
        var sql = query.ToQueryString();
        var projected = await query.SingleAsync();

        sql.ShouldContain("ORDER BY");
        counter.Commands.ShouldBe(1);
        projected.Name.ShouldBe("Ann");
        projected.Code.ShouldBe("stored");
        projected.ShipCity.ShouldBe("Osaka");
        projected.Address!.Street.ShouldBe("1 Main");
        projected.Address.City.ShouldBeEmpty();
        projected.Lines.Count.ShouldBe(2);
        projected.Lines[0].Id.ShouldBe(entity.Lines[1].Id);
        projected.Lines[0].Subtotal.ShouldBe(4m);
        projected.Lines[0].Key.ShouldBe(entity.Lines[1].Id);
        projected.Lines[1].Subtotal.ShouldBe(6m);

        var inMemory = mapper.ToDto(entity);
        inMemory.Lines[0].Id.ShouldBe(projected.Lines[0].Id);
        inMemory.Lines[0].Subtotal.ShouldBe(projected.Lines[0].Subtotal);
        var desired = mapper.ToDto(entity);
        desired.Lines.Reverse();
        context.Entry(entity).Collection(order => order.Lines).IsLoaded = true;
        mapper.Apply(context, entity, mapper.ToDto(entity).CreateChangeSet(desired));
        entity.Lines[0].Id.ShouldBe(projected.Lines[1].Id);
        entity.Lines[0].SortOrder.ShouldBe(0);
    }

    [Test]
    public async Task NullReferencesAndCollectionsProjectWithoutThrowing()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Code = "stored",
                    Address = null,
                    Lines = [],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new ProjectionOrderMapper(),
            context.Model
        );
        var projected = await context
            .Orders.Where(order => order.Id == entity.Id)
            .Select(mapper.Projection)
            .SingleAsync();

        projected.Address.ShouldBeNull();
        projected.ShipCity.ShouldBeNull();
        projected.Lines.ShouldBeEmpty();

        var inMemory = mapper.ToDto(new OrderEntity { Address = null, Lines = null! });

        inMemory.Address.ShouldBeNull();
        inMemory.ShipCity.ShouldBeNull();
        inMemory.Lines.ShouldBeEmpty();
    }

    [Test]
    public async Task SoftDeleteProjectionFiltersRowsWithRegisteredExpression()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .SoftOrders.Add(
                new SoftOrderEntity
                {
                    Lines =
                    [
                        new SoftLineEntity { Qty = 1, IsDeleted = false },
                        new SoftLineEntity { Qty = 2, IsDeleted = true },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var mapper = SparseEntityMapper<SoftOrderEntity, SoftOrderDto>.Create(
            new SoftOrderReadMapper(),
            context.Model
        );

        var query = context
            .SoftOrders.Where(order => order.Id == entity.Id)
            .Select(mapper.Projection);
        var sql = query.ToQueryString();
        var projected = await query.SingleAsync();

        sql.ShouldContain("IsDeleted");
        projected.Lines.Count.ShouldBe(1);
        projected.Lines[0].Qty.ShouldBe(1);
    }

    [Test]
    public async Task PrimitiveCollectionProjectionTranslatesAndPreservesNull()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = context.Tags.Add(new TagsEntity { Tags = ["a", "b"] }).Entity;
        await context.SaveChangesAsync();
        var mapper = SparseEntityMapper<TagsEntity, TagsDto>.Create(
            new CollectionAndCacheTests.TagsMapper(),
            context.Model
        );
        counter.Reset();

        var query = context.Tags.Where(tags => tags.Id == entity.Id).Select(mapper.Projection);
        var sql = query.ToQueryString();
        var projected = await query.SingleAsync();

        sql.ShouldContain("Tags");
        projected.Tags.ShouldBe(["a", "b"]);
        mapper.ToDto(new TagsEntity { Tags = null }).Tags.ShouldBeNull();

        counter.Commands.ShouldBe(1);
    }

    [Test]
    public void ProjectionValidationIsDeferredAndAggregatesMissingReadMappings()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new UnsupportedProjectionMapper(),
            context.Model
        );

        var error = Should.Throw<SparseEntityMappingException>(() => _ = mapper.Projection);

        error.Errors.Count.ShouldBe(2);
        error.Errors.Select(item => item.DtoPath).ShouldContain("Name");
        error.Errors.Select(item => item.DtoPath).ShouldContain("Code");
    }

    [Test]
    public void ProjectionRequiresExpressionOrderAndSoftDeleteFilters()
    {
        using var connection = TestDatabases.OpenMemory();
        using var sample = TestDatabases.CreateSample(connection, new QueryCounter());
        var orderMapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            sample.Model
        );

        var orderError = Should.Throw<SparseEntityMappingException>(() =>
            _ = orderMapper.Projection
        );

        orderError.Errors.Select(item => item.DtoPath).ShouldContain("Lines");

        using var keyed = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var softMapper = SparseEntityMapper<SoftOrderEntity, SoftOrderDto>.Create(
            new KeyedCollectionPolicyTests.SoftOrderMapper(),
            keyed.Model
        );

        var softError = Should.Throw<SparseEntityMappingException>(() => _ = softMapper.Projection);

        softError.Errors.Select(item => item.DtoPath).ShouldContain("Lines");
    }

    [Test]
    public void ReadRegistrationProjectsMembersHandledByCustomWriters()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var mapper = SparseEntityMapper<OrderEntity, ExplicitReadDto>.Create(
            new ExplicitReadMapper(),
            context.Model
        );

        var projected = mapper.ToDto(new OrderEntity { CustomerName = "Ann" });

        projected.Value.ShouldBe("Ann");
    }
}

[SparseFragmentMapper<OrderEntity, OrderDto>]
internal sealed partial class ProjectionOrderMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Name, entity => entity.CustomerName);
        Map.Member(dto => dto.Code, entity => entity.Code, code => code?.ToUpperInvariant());
        Map.Read(dto => dto.Code, entity => entity.Code);
        Map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        Map.Object(
            dto => dto.Address,
            entity => entity.Address,
            child => child.Ignore(address => address.City)
        );
        Map.Ignore(dto => dto.DisplayTotal);
        Map.Collection<OrderLineDto, OrderLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(line => line.Subtotal);
                    child.Read(line => line.Subtotal, entity => entity.Quantity * entity.UnitPrice);
                });
                lines.Delete();
                lines.Order(entity => entity.SortOrder);
            }
        );
    }
}

[SparseFragmentMapper<SoftOrderEntity, SoftOrderDto>]
internal sealed partial class SoftOrderReadMapper
{
    private void Configure()
    {
        Map.Collection<SoftLineDto, SoftLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(static _ => { });
                lines.SoftDelete(line => line.IsDeleted = true);
                lines.SoftDeleteFilter(line => line.IsDeleted);
            }
        );
    }
}

[SparseFragmentMapper<OrderEntity, OrderDto>]
internal sealed partial class UnsupportedProjectionMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Name, entity => entity.CustomerName, name => name?.Trim());
        Map.Write(dto => dto.Code, static (_, _) => { });
        Map.Ignore(dto => dto.Notes);
        Map.Ignore(dto => dto.ShipCity);
        Map.Ignore(dto => dto.DisplayTotal);
        Map.Ignore(dto => dto.Address);
        Map.Ignore(dto => dto.Lines);
    }
}

[SparseModel]
internal sealed partial class ExplicitReadDto
{
    public string Value { get; set; } = string.Empty;
}

[SparseFragmentMapper<OrderEntity, ExplicitReadDto>]
internal sealed partial class ExplicitReadMapper
{
    private void Configure()
    {
        Map.Write(dto => dto.Value, static (_, _) => { });
        Map.Read(dto => dto.Value, entity => entity.CustomerName);
    }
}
