using EfCoreSample.Contracts;
using EfCoreSample.Server;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseFragments;
using SparseFragments.EntityFrameworkCore;

// Runnable Contracts (EF-free DTOs) + Server (entity/mapper/DbContext) demo:
// read current DTO -> Rebase -> direct Apply (no final DTO) -> SaveChangesAsync
// -> authoritative DTO with TemporaryId correlation -> client Reconcile.
using var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

var options = new DbContextOptionsBuilder<SampleDbContext>().UseSqlite(connection).Options;
using (var seed = new SampleDbContext(options))
{
    seed.Database.EnsureCreated();
    seed.Orders.Add(
        new OrderEntity
        {
            CustomerName = "Ann",
            Notes = "keep",
            Code = "abc",
            Address = new AddressEntity { Street = "Old Street", City = "Osaka" },
            Lines =
            [
                new OrderLineEntity
                {
                    Quantity = 1,
                    UnitPrice = 10m,
                    SortOrder = 0,
                },
                new OrderLineEntity
                {
                    Quantity = 2,
                    UnitPrice = 20m,
                    SortOrder = 1,
                },
            ],
        }
    );
    seed.SaveChanges();
}

using var db = new SampleDbContext(options);
var tracked = db.Orders.Include(static order => order.Lines).Single();

static OrderDto ReadCurrentDto(OrderEntity order) =>
    new()
    {
        Name = order.CustomerName,
        Notes = order.Notes,
        Code = order.Code,
        ShipCity = order.Address?.City ?? string.Empty,
        Address = order.Address is null
            ? null
            : new AddressDto { Street = order.Address.Street, City = order.Address.City },
        Lines = order
            .Lines.OrderBy(static line => line.SortOrder)
            .Select(static line => new OrderLineDto
            {
                Id = line.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Subtotal = line.Quantity * line.UnitPrice,
            })
            .ToList(),
    };

var current = ReadCurrentDto(tracked);
var baselineIds = tracked.Lines.Select(static line => line.Id).ToHashSet();

// Client edit session over a clone of the baseline.
var draft = new OrderDto
{
    Name = current.Name,
    Notes = current.Notes,
    Code = current.Code,
    ShipCity = current.ShipCity,
    Address = current.Address is null
        ? null
        : new AddressDto { Street = current.Address.Street, City = current.Address.City },
    Lines = current
        .Lines.Select(static line => new OrderLineDto
        {
            Id = line.Id,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            Subtotal = line.Subtotal,
        })
        .ToList(),
};
var session = draft.CreateEditSession();
draft.Notes = "updated";
draft.Lines.Single(static line => line.Quantity == 2).Quantity = 5;
draft.Lines.RemoveAll(static line => line.Quantity == 1);
var temporary = Guid.NewGuid();
draft.Lines.Add(
    new OrderLineDto
    {
        Quantity = 7,
        UnitPrice = 70m,
        TemporaryId = temporary,
    }
);
var submitted = session.CreateChangeSet();

// Server: rebase onto the authoritative read, then apply the accepted
// ChangeSet directly. No final DTO is materialized between Rebase and Apply.
var rebase = submitted.RebaseOnto(current);
if (rebase.HasConflicts)
{
    Console.WriteLine("Conflict; authoritative state wins.");
    return 1;
}

var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(new OrderMapper(), db.Model);
var applied = mapper.ApplyWithResult(db, tracked, rebase.Rebased);
Console.WriteLine($"Applied: Notes={tracked.Notes}, Lines={tracked.Lines.Count}");
await db.SaveChangesAsync();

// Authoritative DTO: persisted rows plus temporary correlation for additions.
var authoritative = ReadCurrentDto(tracked);
var createdIds = applied
    .CreatedByPath["Lines"]
    .ToDictionary(static entry => ((OrderLineEntity)entry.Value).Id, static entry => entry.Key);
foreach (var line in authoritative.Lines)
{
    if (!baselineIds.Contains(line.Id) && createdIds.TryGetValue(line.Id, out var temp))
    {
        line.TemporaryId = temp;
    }
}

var reconciled = session.TryReconcile(submitted, authoritative, out _);
Console.WriteLine(
    $"Reconciled={reconciled}, addition persisted as Id={authoritative.Lines.Single(static line => line.Quantity == 7).Id}"
);
return reconciled ? 0 : 2;
