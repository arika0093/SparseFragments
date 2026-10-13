using Microsoft.EntityFrameworkCore;
using SparseFragments.EntityFrameworkCore.Tests.Sample;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>Collection diagnosis, plan caching, multi-config mapping, and assembly boundaries.</summary>
public sealed class CollectionAndCacheTests
{
    [Test]
    public async Task KeyedCollectionChangeIsDiagnosed()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = context
            .Carts.Add(new CartEntity { Lines = [new CartLineEntity { Sku = "a", Qty = 1 }] })
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = new CartDto { Lines = [new CartLineDto { Sku = "a", Qty = 1 }] };
        var incoming = new CartDto
        {
            Lines =
            [
                new CartLineDto { Sku = "a", Qty = 2 },
                new CartLineDto { Sku = "b", Qty = 1 },
            ],
        };
        var accepted = current.CreateChangeSet(incoming);
        accepted.IsChanged.ShouldBeTrue();

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<CartEntity, CartDto>
                .Create(new CartMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionNotSupported);
        error.DtoPath.ShouldBe("Lines");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task PrimitiveCollectionChangeReplacesWithFreshCollection()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = context.Tags.Add(new TagsEntity { Tags = ["a", "b"] }).Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = new TagsDto { Tags = ["a", "b"] };
        var incoming = new TagsDto { Tags = ["c"] };
        var accepted = current.CreateChangeSet(incoming);
        accepted.IsChanged.ShouldBeTrue();
        var original = entity.Tags;

        SparseEntityMapper<TagsEntity, TagsDto>
            .Create(new TagsMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Tags.ShouldBe(["c"]);
        ReferenceEquals(original, entity.Tags).ShouldBeFalse();
        context.Entry(entity).Property(static tags => tags.Tags).IsModified.ShouldBeTrue();
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        var stored = await context.Tags.AsNoTracking().SingleAsync(tags => tags.Id == entity.Id);
        stored.Tags.ShouldBe(["c"]);
    }

    [Test]
    public async Task PrimitiveCollectionNullAndMissingRemainDistinct()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = context.Tags.Add(new TagsEntity { Tags = ["keep"] }).Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = new TagsDto { Tags = ["keep"] };
        var incoming = new TagsDto { Tags = null };
        var nullChange = current.CreateChangeSet(incoming);
        SparseEntityMapper<TagsEntity, TagsDto>
            .Create(new TagsMapper(), context.Model)
            .Apply(context, entity, nullChange);
        entity.Tags.ShouldBeNull();
        context.Entry(entity).Property(static tags => tags.Tags).IsModified.ShouldBeTrue();
        counter.Commands.ShouldBe(0);
        await context.SaveChangesAsync();
        var storedNull = await context
            .Tags.AsNoTracking()
            .SingleAsync(tags => tags.Id == entity.Id);
        storedNull.Tags.ShouldBeNull();

        entity.Tags = ["keep"];
        await context.SaveChangesAsync();
        counter.Reset();
        var basis = Optional<TagsDto.Fragment?>.Present(TagsDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Tags = Optional<List<string>?>.Missing;
        var missingChange = TagsDto.ChangeSet.Between(
            basis,
            Optional<TagsDto.Fragment?>.Present(builder.Build())
        );
        SparseEntityMapper<TagsEntity, TagsDto>
            .Create(new TagsIgnoreMissingMapper(), context.Model)
            .Apply(context, entity, missingChange);
        entity.Tags.ShouldBe(["keep"]);
        context.Entry(entity).Property(static tags => tags.Tags).IsModified.ShouldBeFalse();
        counter.Commands.ShouldBe(0);
    }

    /// <summary>Whole-value tag mapping, which still diagnoses in this proof of concept.</summary>
    [SparseFragmentMapper]
    public sealed class TagsMapper : IEntityModelMapping<TagsEntity, TagsDto>
    {
        public void Configure(SparseEntityMapBuilder<TagsEntity, TagsDto> map)
        {
            map.Collection(dto => dto.Tags, entity => entity.Tags);
        }
    }

    [SparseFragmentMapper]
    public sealed class TagsIgnoreMissingMapper : IEntityModelMapping<TagsEntity, TagsDto>
    {
        public void Configure(SparseEntityMapBuilder<TagsEntity, TagsDto> map)
        {
            map.Collection(dto => dto.Tags, entity => entity.Tags);
            map.OnMissing(dto => dto.Tags, SparseEntityMissingPolicy.Ignore);
        }
    }

    [Test]
    public void SameModelBuildsIndependentMappersPerInstance()
    {
        // Regression for the former static plan cache keyed by mapping CLR type:
        // two mapping instances with different configuration must never share
        // one compiled plan (the first instance's converters leaked into the
        // second). Each Create builds and validates a fresh plan; callers reuse
        // the returned mapper instance for repeated applies.
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var first = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
            new WidgetMapper(),
            context.Model
        );
        var second = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
            new WidgetMapper(),
            context.Model
        );
        ReferenceEquals(first, second).ShouldBeFalse();
    }

    [Test]
    public void DifferentModelsBuildDifferentPlans()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        using var variant = TestDatabases.CreateVariant(connection, counter);
        ReferenceEquals(context.Model, variant.Model).ShouldBeFalse();

        var first = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
            new WidgetMapper(),
            context.Model
        );
        var second = SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
            new WidgetMapper(),
            variant.Model
        );
        ReferenceEquals(first, second).ShouldBeFalse();

        ApplyRename(context, first);
        ApplyRename(variant, second);

        static void ApplyRename(
            DbContext context,
            SparseEntityMapper<WidgetEntity, WidgetDto> mapper
        )
        {
            var entity = new WidgetEntity("Gizmo", "active");
            context.Add(entity);
            var current = new WidgetDto
            {
                Id = entity.Id,
                Name = "Gizmo",
                Status = "active",
                RowVersion = entity.RowVersion,
            };
            var incoming = new WidgetDto
            {
                Id = entity.Id,
                Name = "Renamed",
                Status = "active",
                RowVersion = entity.RowVersion,
            };
            var accepted = current.CreateChangeSet(incoming);
            mapper.Apply(context, entity, accepted);
            entity.Name.ShouldBe("Renamed");
        }
    }

    [Test]
    public async Task OneEntityTwoDtoConfigs()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = new OrderSummaryDto { Title = entity.CustomerName, Notes = entity.Notes };
        var incoming = new OrderSummaryDto { Title = "Summary Bob", Notes = entity.Notes };
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<OrderEntity, OrderSummaryDto>
            .Create(new OrderSummaryMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.CustomerName.ShouldBe("Summary Bob");
        entity.Code.ShouldBe("ABC");
        entity.Address.ShouldNotBeNull();
        entity.Address!.City.ShouldBe("Osaka");
        var modified = context
            .Entry(entity)
            .Properties.Where(static property => property.IsModified)
            .Select(static property => property.Metadata.Name)
            .ToList();
        modified.ShouldBe(["CustomerName"]);
        counter.Commands.ShouldBe(0);

        // Apply alone persists nothing: a fresh context still sees the old row.
        var verifyOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;
        using (var verify = new SampleDbContext(verifyOptions))
        {
            verify.Orders.Single(order => order.Id == entity.Id).CustomerName.ShouldBe("Ann");
        }

        await context.SaveChangesAsync();
        using (var verify = new SampleDbContext(verifyOptions))
        {
            verify
                .Orders.Single(order => order.Id == entity.Id)
                .CustomerName.ShouldBe("Summary Bob");
        }
    }
}
