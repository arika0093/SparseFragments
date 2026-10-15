using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Nested scope identity without display-text correlation.</summary>
/// <remarks>Nested temporary-identity correlation resolves by parent scope (stable key or temporary identity with the configured comparer).</remarks>
public sealed class NestedScopeIdentityTests
{
    private static BinHolderDto ReadBins(BinHolderEntity holder) =>
        new()
        {
            Bins = holder
                .Bins.Select(static bin => new BinDto
                {
                    Code = new BinCode(bin.Aisle, bin.Bay),
                    Lines = bin
                        .Lines.Select(static line => new BinLineDto
                        {
                            Id = line.Id,
                            Qty = line.Qty,
                        })
                        .ToList(),
                })
                .ToList(),
        };

    private static SparseEntityMapper<BinHolderEntity, BinHolderDto> CreateMapper(
        KeyedMappingContext context
    ) =>
        SparseEntityMapper<BinHolderEntity, BinHolderDto>.Create(
            new BinHolderMapper(),
            context.Model
        );

    [Test]
    public async Task DistinctStructKeysWithIdenticalDisplayTextReconcileNestedTemps()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .BinHolders.Add(
                new BinHolderEntity
                {
                    Bins =
                    [
                        new BinEntity { Aisle = "a", Bay = 1 },
                        new BinEntity { Aisle = "a", Bay = 2 },
                        // Baseline-only row: never touched by the submission.
                        new BinEntity
                        {
                            Aisle = "b",
                            Bay = 9,
                            Lines = [new BinLineEntity { Qty = 7 }],
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static holder => holder.Bins).LoadAsync();
        foreach (var bin in entity.Bins)
        {
            await context.Entry(bin).Collection(static b => b.Lines).LoadAsync();
        }

        counter.Reset();

        // Every BinCode renders the same display text (default ValueType
        // ToString), so path strings alone cannot tell the bins apart.
        var shared = Guid.NewGuid();
        var current = ReadBins(entity);
        var incoming = ReadBins(entity);
        incoming.Bins[0].Lines.Add(new BinLineDto { Qty = 1, TemporaryId = shared });
        incoming.Bins[1].Lines.Add(new BinLineDto { Qty = 2, TemporaryId = shared });
        var accepted = current.CreateChangeSet(incoming);

        var mapper = CreateMapper(context);
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        var authoritative = ReadBins(entity);
        mapper.AssignTemporaryKeys(context, result, current, authoritative);

        var first = authoritative.Bins[0].Lines.Single();
        var second = authoritative.Bins[1].Lines.Single();
        first.TemporaryId.ShouldBe(shared);
        second.TemporaryId.ShouldBe(shared);
        first.Id.ShouldNotBeNull();
        second.Id.ShouldNotBeNull();
        first.Id.ShouldNotBe(second.Id);

        // Baseline-only rows stay untouched and carry no temporary identity.
        var untouched = authoritative.Bins[2].Lines.Single();
        untouched.Id.ShouldNotBeNull();
        untouched.TemporaryId.ShouldBeNull();
        untouched.Qty.ShouldBe(7);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task DelimiterKeysAndReorderKeepNestedCorrelation()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .BinHolders.Add(
                new BinHolderEntity
                {
                    Bins =
                    [
                        new BinEntity { Aisle = "a[0].b", Bay = 1 },
                        new BinEntity { Aisle = "日本語/x.y", Bay = 2 },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static holder => holder.Bins).LoadAsync();
        foreach (var bin in entity.Bins)
        {
            await context.Entry(bin).Collection(static b => b.Lines).LoadAsync();
        }

        counter.Reset();

        var shared = Guid.NewGuid();
        var current = ReadBins(entity);
        var incoming = ReadBins(entity);
        incoming.Bins[0].Lines.Add(new BinLineDto { Qty = 1, TemporaryId = shared });
        incoming.Bins[1].Lines.Add(new BinLineDto { Qty = 2, TemporaryId = shared });
        var accepted = current.CreateChangeSet(incoming);

        var mapper = CreateMapper(context);
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        // The authority lists bins in reverse order: typed correlation must
        // not depend on collection position.
        var authoritative = ReadBins(entity);
        authoritative.Bins.Reverse();
        mapper.AssignTemporaryKeys(context, result, current, authoritative);

        var first = authoritative.Bins.Single(bin => bin.Code.Bay == 1).Lines.Single();
        var second = authoritative.Bins.Single(bin => bin.Code.Bay == 2).Lines.Single();
        first.TemporaryId.ShouldBe(shared);
        second.TemporaryId.ShouldBe(shared);
        first.Id.ShouldNotBeNull();
        second.Id.ShouldNotBeNull();
        first.Id.ShouldNotBe(second.Id);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task TemporaryParentsResolveNestedScopesThroughFallback()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Parcels.Add(new ParcelEntity { Lines = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static parcel => parcel.Lines).LoadAsync();
        counter.Reset();

        // Both parents are unassigned at apply time.
        var parentA = Guid.NewGuid();
        var parentB = Guid.NewGuid();
        var nested = Guid.NewGuid();
        var current = new ParcelDto { Lines = [] };
        var incoming = new ParcelDto
        {
            Lines =
            [
                new ParcelLineDto
                {
                    Qty = 1,
                    TemporaryId = parentA,
                    Tags = [new ParcelTagDto { Label = "a", TemporaryId = nested }],
                },
                new ParcelLineDto
                {
                    Qty = 2,
                    TemporaryId = parentB,
                    Tags = [new ParcelTagDto { Label = "b", TemporaryId = nested }],
                },
            ],
        };
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<ParcelEntity, ParcelDto>.Create(
            new DirectApplyRegressionTests.ParcelMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        var authoritative = new ParcelDto
        {
            Lines = entity
                .Lines.Select(static line => new ParcelLineDto
                {
                    Id = line.Id,
                    Qty = line.Qty,
                    Tags = line
                        .Tags.Select(static tag => new ParcelTagDto
                        {
                            Id = tag.Id,
                            Label = tag.Label,
                        })
                        .ToList(),
                })
                .ToList(),
        };
        mapper.AssignTemporaryKeys(context, result, current, authoritative);

        var firstTags = authoritative.Lines.Single(line => line.Qty == 1).Tags;
        var secondTags = authoritative.Lines.Single(line => line.Qty == 2).Tags;
        firstTags.ShouldHaveSingleItem().TemporaryId.ShouldBe(nested);
        secondTags.ShouldHaveSingleItem().TemporaryId.ShouldBe(nested);
        firstTags[0].Id.ShouldNotBe(secondTags[0].Id);
        counter.Commands.ShouldBe(0);
    }
}
