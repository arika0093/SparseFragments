using Microsoft.EntityFrameworkCore;
using SparseTypes.Playground.Models;

namespace SparseTypes.Tests.Generation;

public sealed class Case5EfMappingTests
{
    [Test]
    public void MapperProjectionPreservesNullNotesAndOrdersLines()
    {
        var options = CreateOptions();
        using var db = new PlaygroundEfContext(options);
        db.Database.EnsureCreated();
        var mapper = PlaygroundOrderMapper.Create(db.Model);

        var dto = mapper.ToDto(
            new PlaygroundOrderEntity
            {
                CustomerName = "Cafe North",
                Total = 15m,
                Note = null,
                Lines =
                [
                    new PlaygroundOrderLineEntity
                    {
                        Id = 22,
                        Product = "B",
                        Qty = 2,
                        SortOrder = 1,
                    },
                    new PlaygroundOrderLineEntity
                    {
                        Id = 11,
                        Product = "A",
                        Qty = 1,
                        SortOrder = 0,
                    },
                ],
            }
        );

        dto.Customer.ShouldBe("Cafe North");
        dto.Notes.ShouldBe(string.Empty);
        dto.Total.ShouldBe(15m);
        dto.Lines.Select(static line => line.Id).ShouldBe([11, 22]);
        dto.Lines.Select(static line => line.Product).ShouldBe(["A", "B"]);

        var withNote = mapper.ToDto(
            new PlaygroundOrderEntity
            {
                CustomerName = "Cafe South",
                Note = new PlaygroundNoteEntity { Text = "follow up" },
            }
        );
        withNote.Notes.ShouldBe("follow up");
    }

    [Test]
    public void LineCollectionEditorPreservesAssignedAndTemporaryIdentities()
    {
        var temporaryId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var model = new PlaygroundOrderDto
        {
            Lines =
            [
                new PlaygroundOrderLineDto
                {
                    Id = 11,
                    Product = "A",
                    Qty = 1,
                },
                new PlaygroundOrderLineDto
                {
                    Id = 22,
                    Product = "B",
                    Qty = 2,
                },
            ],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundOrderLineDto, int>(
            PlaygroundOrderDto.SparsePath.Lines
        );

        editor
            .TryAdd(
                new PlaygroundOrderLineDto
                {
                    Product = "new",
                    Qty = 3,
                    TemporaryId = temporaryId,
                },
                out var addError
            )
            .ShouldBeTrue($"add error: {addError}");
        editor.TryMoveByKey(22, 0, out var moveError).ShouldBeTrue($"move error: {moveError}");
        editor
            .TryReplaceByKey(
                11,
                new PlaygroundOrderLineDto
                {
                    Id = 11,
                    Product = "replaced",
                    Qty = 4,
                },
                out var replaceError
            )
            .ShouldBeTrue($"replace error: {replaceError}");
        editor
            .TryRemoveByTemporaryKey(temporaryId, out var removeError)
            .ShouldBeTrue($"remove error: {removeError}");

        model.Lines.Select(static line => line.Id).ShouldBe([22, 11]);
        model.Lines[1].Product.ShouldBe("replaced");
        session.CreateChangeSet().IsChanged.ShouldBeTrue();
    }

    [Test]
    public void ControlledThirdPartyUpdatesSupportPerPathResolutionAndLateDraftEdits()
    {
        var options = CreateOptions();
        using var seedDb = new PlaygroundEfContext(options);
        seedDb.Database.EnsureCreated();
        var order = seedDb
            .Orders.Add(
                new PlaygroundOrderEntity
                {
                    CustomerName = "Cafe Vienna",
                    Total = 13.75m,
                    Revision = 1,
                    Note = new PlaygroundNoteEntity { Text = "first order" },
                    Lines =
                    [
                        new PlaygroundOrderLineEntity
                        {
                            Product = "Espresso",
                            Qty = 2,
                            SortOrder = 0,
                        },
                    ],
                }
            )
            .Entity;
        seedDb.SaveChanges();
        var mapper = PlaygroundOrderMapper.Create(seedDb.Model);
        var orderId = order.Id;

        PlaygroundOrderDto initial;
        using (var readDb = new PlaygroundEfContext(options))
        {
            var loaded = readDb
                .Orders.Include(static item => item.Note)
                .Include(static item => item.Lines)
                .Single(item => item.Id == orderId);
            initial = mapper.ToDto(loaded);
        }

        var session = initial.CreateEditSession();
        initial.Customer = "Submitted customer";
        initial.Notes = "Submitted note";
        initial.Total = 20m;
        var submitted = session.CreateChangeSet();
        var submittedDraft = initial.DeepClone();
        initial.Total = 21m;

        SetCurrentValue(options, orderId, true, "Current customer").ShouldBe(2);
        SetCurrentValue(options, orderId, false, "Current note").ShouldBe(3);
        initial.Customer.ShouldBe("Submitted customer");
        initial.Notes.ShouldBe("Submitted note");

        using var applyDb = new PlaygroundEfContext(options);
        var currentEntity = applyDb
            .Orders.Include(static item => item.Note)
            .Include(static item => item.Lines)
            .Single(item => item.Id == orderId);
        var current = mapper.ToDto(currentEntity);
        var rebase = submitted.RebaseOnto(current);
        rebase.HasConflicts.ShouldBeTrue();
        rebase.Conflicts.Select(static conflict => conflict.PathText).ShouldContain("Customer");
        rebase.Conflicts.Select(static conflict => conflict.PathText).ShouldContain("Notes");

        var resolution = PlaygroundOrderDto.ChangeSet.BeginResolution(rebase, current);
        resolution.UseCurrent(PlaygroundOrderDto.SparsePath.Customer);
        resolution.UseIncoming(PlaygroundOrderDto.SparsePath.Notes);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");
        resolved.ShouldNotBeNull();

        var result = mapper.ApplyWithResult(applyDb, currentEntity, resolved!);
        currentEntity.Revision++;
        applyDb.SaveChanges();
        applyDb.ChangeTracker.Clear();
        var persistedEntity = applyDb
            .Orders.Include(static item => item.Note)
            .Include(static item => item.Lines)
            .Single(item => item.Id == orderId);
        var persisted = mapper.ToDto(persistedEntity);
        mapper.AssignTemporaryKeys(applyDb, result, submittedDraft, persisted);

        var postSubmit = submittedDraft.CreateChangeSet(initial);
        var postSubmitRebase = postSubmit.RebaseOnto(persisted);
        var targetDraft = postSubmitRebase
            .Rebased.ToPatch()
            .Apply(
                Optional<PlaygroundOrderDto.Fragment?>.Present(
                    PlaygroundOrderDto.Fragment.From(persisted)
                )
            )
            .Value!.ToModel();
        var syncDraft = PlaygroundOrderDto.ChangeSet.Between(initial, targetDraft);
        session
            .TryApplyInPlace(syncDraft, out var syncConflicts)
            .ShouldBeTrue($"sync conflicts: {syncConflicts?.Count ?? 0}");
        session
            .TryReconcile(submitted, persisted, out var reconcileError)
            .ShouldBeTrue($"reconcile error: {reconcileError}");
        initial.Customer.ShouldBe("Current customer");
        initial.Notes.ShouldBe("Submitted note");
        initial.Total.ShouldBe(21m);
        session.CreateChangeSet().IsChanged.ShouldBeTrue();
        persisted.Total.ShouldBe(20m);
        persistedEntity.Revision.ShouldBe(4);
    }

    [Test]
    public void DisjointThirdPartyUpdateIsIncludedInTheRetry()
    {
        var options = CreateOptions();
        using var seedDb = new PlaygroundEfContext(options);
        seedDb.Database.EnsureCreated();
        var order = seedDb
            .Orders.Add(
                new PlaygroundOrderEntity
                {
                    CustomerName = "Cafe Vienna",
                    Total = 13.75m,
                    Revision = 1,
                    Note = new PlaygroundNoteEntity { Text = "first order" },
                }
            )
            .Entity;
        seedDb.SaveChanges();
        var mapper = PlaygroundOrderMapper.Create(seedDb.Model);
        var orderId = order.Id;

        PlaygroundOrderDto baseline;
        using (var readDb = new PlaygroundEfContext(options))
        {
            var loaded = readDb
                .Orders.Include(static item => item.Note)
                .Include(static item => item.Lines)
                .Single(item => item.Id == orderId);
            baseline = mapper.ToDto(loaded);
        }

        var session = baseline.CreateEditSession();
        baseline.Customer = "Local customer";
        var submitted = session.CreateChangeSet();
        SetCurrentValue(options, orderId, false, "Current note").ShouldBe(2);

        using var applyDb = new PlaygroundEfContext(options);
        var currentEntity = applyDb
            .Orders.Include(static item => item.Note)
            .Include(static item => item.Lines)
            .Single(item => item.Id == orderId);
        var current = mapper.ToDto(currentEntity);
        var rebase = submitted.RebaseOnto(current);
        rebase.HasConflicts.ShouldBeFalse();

        mapper.Apply(applyDb, currentEntity, rebase.Rebased);
        currentEntity.Revision++;
        applyDb.SaveChanges();
        applyDb.ChangeTracker.Clear();
        var persistedEntity = applyDb
            .Orders.Include(static item => item.Note)
            .Include(static item => item.Lines)
            .Single(item => item.Id == orderId);
        var persisted = mapper.ToDto(persistedEntity);

        persisted.Customer.ShouldBe("Local customer");
        persisted.Notes.ShouldBe("Current note");
        persistedEntity.Revision.ShouldBe(3);
    }

    private static DbContextOptions<PlaygroundEfContext> CreateOptions() =>
        new DbContextOptionsBuilder<PlaygroundEfContext>()
            .UseInMemoryDatabase($"case5-{Guid.NewGuid():N}")
            .Options;

    private static int SetCurrentValue(
        DbContextOptions<PlaygroundEfContext> options,
        int orderId,
        bool customer,
        string value
    )
    {
        using var db = new PlaygroundEfContext(options);
        var order = db.Orders.Include(static item => item.Note).Single(item => item.Id == orderId);
        if (customer)
        {
            order.CustomerName = value;
        }
        else if (order.Note is not null)
        {
            order.Note.Text = value;
        }
        else
        {
            throw new InvalidOperationException("The seeded order has no note.");
        }

        order.Revision++;
        db.SaveChanges();
        return order.Revision;
    }
}
