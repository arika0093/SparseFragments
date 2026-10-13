using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Validate-and-stage semantics for temporary-key reconciliation (issue #247).</summary>
/// <remarks>
/// Every required assignment across all nested collections must be validated
/// before any authoritative DTO mutation. Duplicate, missing, or ambiguous
/// correspondence fails with <see cref="SparseEntityMappingException" /> and
/// leaves the authoritative DTO untouched.
/// </remarks>
public sealed class TemporaryKeyStagingTests
{
    private static DuoDto ReadDuo(DuoEntity owner) =>
        new()
        {
            Actives = owner
                .Actives.Select(static line => new DuoLineDto
                {
                    Id = line.Id,
                    Qty = line.Qty,
                    Note = line.Note,
                })
                .ToList(),
            Archived = owner
                .Archived.Select(static line => new DuoLineDto
                {
                    Id = line.Id,
                    Qty = line.Qty,
                    Note = line.Note,
                })
                .ToList(),
        };

    private static ParcelDto ReadParcel(ParcelEntity parcel) =>
        new()
        {
            Lines = parcel
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

    [Test]
    public async Task SiblingCollectionsWithReusedGuidsStillReconcile()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Duos.Add(new DuoEntity { Actives = [], Archived = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Actives).LoadAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        counter.Reset();

        // The same temporary value reused under two collection paths resolves
        // independently; scoped reuse is valid and must keep reconciling.
        var shared = Guid.NewGuid();
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives.Add(new DuoLineDto { Qty = 1, TemporaryId = shared });
        incoming.Archived.Add(new DuoLineDto { Qty = 2, TemporaryId = shared });
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        var authoritative = ReadDuo(entity);
        mapper.AssignTemporaryKeys(context, result, current, authoritative);

        authoritative.Actives.Single().TemporaryId.ShouldBe(shared);
        authoritative.Archived.Single().TemporaryId.ShouldBe(shared);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task MissingCreatedRowFailsWithoutMutatingEarlierCollections()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Duos.Add(new DuoEntity { Actives = [], Archived = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Actives).LoadAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        counter.Reset();

        var activeTemporary = Guid.NewGuid();
        var archivedTemporary = Guid.NewGuid();
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives.Add(new DuoLineDto { Qty = 1, TemporaryId = activeTemporary });
        incoming.Archived.Add(new DuoLineDto { Qty = 2, TemporaryId = archivedTemporary });
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        // Simulate data loss: the archived created row never reaches the
        // authoritative DTO while the earlier Actives scope would reconcile.
        var authoritative = ReadDuo(entity);
        authoritative.Archived.Clear();

        var error = Should.Throw<SparseEntityMappingException>(() =>
            mapper.AssignTemporaryKeys(context, result, current, authoritative)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);
        (error.DtoPath ?? string.Empty).ShouldContain("Archived");
        authoritative.Actives.Single().TemporaryId.ShouldBeNull();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task DuplicateAssignedIdentityInAuthoritativeFailsWithoutMutation()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Duos.Add(new DuoEntity { Actives = [], Archived = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Actives).LoadAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        counter.Reset();

        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives.Add(new DuoLineDto { Qty = 1, TemporaryId = Guid.NewGuid() });
        incoming.Actives.Add(new DuoLineDto { Qty = 2, TemporaryId = Guid.NewGuid() });
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        // Two authoritative rows claim one persisted identity: correlation is
        // ambiguous and must fail before any temporary is assigned.
        var authoritative = ReadDuo(entity);
        authoritative.Actives[1].Id = authoritative.Actives[0].Id;

        var error = Should.Throw<SparseEntityMappingException>(() =>
            mapper.AssignTemporaryKeys(context, result, current, authoritative)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyConflict);
        (error.DtoPath ?? string.Empty).ShouldContain("Actives");
        authoritative.Actives[0].TemporaryId.ShouldBeNull();
        authoritative.Actives[1].TemporaryId.ShouldBeNull();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task DuplicateTemporaryInAuthoritativeScopeFailsWithoutMutation()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Duos.Add(new DuoEntity { Actives = [], Archived = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Actives).LoadAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        counter.Reset();

        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives.Add(new DuoLineDto { Qty = 1, TemporaryId = Guid.NewGuid() });
        incoming.Actives.Add(new DuoLineDto { Qty = 2, TemporaryId = Guid.NewGuid() });
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        // Two authoritative rows claim one temporary identity within a single
        // scope: the duplicate must be diagnosed, never assigned by position.
        var shared = Guid.NewGuid();
        var authoritative = ReadDuo(entity);
        authoritative.Actives[0].TemporaryId = shared;
        authoritative.Actives[1].TemporaryId = shared;

        var error = Should.Throw<SparseEntityMappingException>(() =>
            mapper.AssignTemporaryKeys(context, result, current, authoritative)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyConflict);
        (error.DtoPath ?? string.Empty).ShouldContain("Actives");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task DeepNestedFailureLeavesEarlierMembersUnmodified()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .Parcels.Add(new ParcelEntity { Lines = [new ParcelLineEntity { Qty = 9, Tags = [] }] })
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static parcel => parcel.Lines).LoadAsync();
        counter.Reset();

        var firstLine = Guid.NewGuid();
        var secondLine = Guid.NewGuid();
        var sharedTag = Guid.NewGuid();
        var current = ReadParcel(entity);
        var baselineTemporary = Guid.NewGuid();
        current.Lines[0].TemporaryId = baselineTemporary;
        var incoming = ReadParcel(entity);
        incoming.Lines[0].TemporaryId = baselineTemporary;
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 1,
                TemporaryId = firstLine,
                Tags = [new ParcelTagDto { TemporaryId = sharedTag, Label = "one" }],
            }
        );
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 2,
                TemporaryId = secondLine,
                Tags = [new ParcelTagDto { TemporaryId = sharedTag, Label = "two" }],
            }
        );
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<ParcelEntity, ParcelDto>.Create(
            new DirectApplyRegressionTests.ParcelJunkMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        // The survivor and the first added line stage cleanly, but the second
        // added line loses its nested tag row: the deep failure must roll back
        // every earlier staged assignment in the authoritative DTO.
        var authoritative = ReadParcel(entity);
        authoritative.Lines.Single(static line => line.Qty == 2).Tags.Clear();

        var error = Should.Throw<SparseEntityMappingException>(() =>
            mapper.AssignTemporaryKeys(context, result, current, authoritative)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);
        (error.DtoPath ?? string.Empty).ShouldContain("Tags");
        foreach (var line in authoritative.Lines)
        {
            line.TemporaryId.ShouldBeNull();
            foreach (var tag in line.Tags)
            {
                tag.TemporaryId.ShouldBeNull();
            }
        }

        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task ReorderedAuthoritativeStillReconcilesByKey()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Duos.Add(new DuoEntity { Actives = [], Archived = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Actives).LoadAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        counter.Reset();

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives.Add(new DuoLineDto { Qty = 1, TemporaryId = first });
        incoming.Actives.Add(new DuoLineDto { Qty = 2, TemporaryId = second });
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        // Order carries no identity: a reordered authoritative DTO reconciles
        // the same temporary identities by stable key, never by position.
        var authoritative = ReadDuo(entity);
        authoritative.Actives.Reverse();

        mapper.AssignTemporaryKeys(context, result, current, authoritative);

        authoritative.Actives.Single(static line => line.Qty == 1).TemporaryId.ShouldBe(first);
        authoritative.Actives.Single(static line => line.Qty == 2).TemporaryId.ShouldBe(second);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NestedAssignedAndUnassignedParentsReconcile()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .Parcels.Add(new ParcelEntity { Lines = [new ParcelLineEntity { Qty = 9, Tags = [] }] })
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static parcel => parcel.Lines).LoadAsync();
        counter.Reset();

        // One survivor with an assigned key plus one unassigned parent carrying
        // a nested tag: both scopes reconcile in a single staged pass.
        var survivorTemporary = Guid.NewGuid();
        var addedLine = Guid.NewGuid();
        var addedTag = Guid.NewGuid();
        var current = ReadParcel(entity);
        current.Lines[0].TemporaryId = survivorTemporary;
        var incoming = ReadParcel(entity);
        incoming.Lines[0].TemporaryId = survivorTemporary;
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 4,
                TemporaryId = addedLine,
                Tags = [new ParcelTagDto { TemporaryId = addedTag, Label = "four" }],
            }
        );
        var accepted = current.CreateChangeSet(incoming);

        var mapper = SparseEntityMapper<ParcelEntity, ParcelDto>.Create(
            new DirectApplyRegressionTests.ParcelJunkMapper(),
            context.Model
        );
        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        counter.Reset();

        var authoritative = ReadParcel(entity);
        mapper.AssignTemporaryKeys(context, result, current, authoritative);

        authoritative
            .Lines.Single(static line => line.Qty == 9)
            .TemporaryId.ShouldBe(survivorTemporary);
        authoritative.Lines.Single(static line => line.Qty == 4).TemporaryId.ShouldBe(addedLine);
        authoritative
            .Lines.Single(static line => line.Qty == 4)
            .Tags.Single()
            .TemporaryId.ShouldBe(addedTag);
        counter.Commands.ShouldBe(0);
    }
}
