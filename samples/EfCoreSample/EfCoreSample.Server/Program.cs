using EfCoreSample.Contracts;
using EfCoreSample.Server;
using Microsoft.EntityFrameworkCore;
using SparseFragments.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5000");
builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("orders") ?? "Data Source=orders.db"
    )
);
builder.Services.AddSparseEntityMappers(typeof(OrderMapper));
var app = builder.Build();
app.Services.ValidateSparseEntityMappers<SampleDbContext>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureCreated();
    if (!db.Orders.Any())
    {
        db.Orders.Add(
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
        db.SaveChanges();
    }
}

app.MapGet("/orders/{id:int}", GetOrderAsync);
app.MapPut("/orders/{id:int}", UpdateOrderAsync);
app.MapGet("/health", () => Results.Ok());
app.Run();

static async Task<IResult> GetOrderAsync(
    int id,
    SampleDbContext db,
    ISparseEntityMapper<OrderEntity, OrderDto> mapper,
    ILogger<Program> logger,
    CancellationToken cancellationToken
)
{
    logger.LogInformation("Loading order {OrderId}", id);
    var entity = await db
        .Orders.Include(static order => order.Address)
        .Include(static order => order.Lines)
        .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
    if (entity is null)
    {
        logger.LogWarning("Order {OrderId} was not found", id);
        return Results.NotFound();
    }

    logger.LogInformation("Loaded order {OrderId} with {LineCount} lines", id, entity.Lines.Count);
    return Results.Ok(mapper.ToDto(db, entity));
}

static async Task<IResult> UpdateOrderAsync(
    int id,
    OrderDto.ChangePayload payload,
    SampleDbContext db,
    ISparseEntityMapper<OrderEntity, OrderDto> mapper,
    ILogger<Program> logger,
    CancellationToken cancellationToken
)
{
    logger.LogInformation("Received change payload for order {OrderId}", id);
    var entity = await db
        .Orders.Include(static order => order.Address)
        .Include(static order => order.Lines)
        .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
    if (entity is null)
    {
        logger.LogWarning("Cannot update order {OrderId} because it was not found", id);
        return Results.NotFound();
    }

    var current = mapper.ToDto(db, entity);
    OrderDto.ChangeSet changes;
    try
    {
        changes = payload.ToChangeSet();
    }
    catch (ArgumentException exception)
    {
        logger.LogWarning(exception, "Rejected invalid change payload for order {OrderId}", id);
        return Results.BadRequest();
    }

    var accepted = changes.RebaseOnto(current);
    if (accepted.HasConflicts)
    {
        logger.LogWarning("Rejected update for order {OrderId} because it has conflicts", id);
        return Results.Conflict();
    }

    var applied = mapper.ApplyWithResult(db, entity, accepted.Rebased);
    await db.SaveChangesAsync(cancellationToken);

    var authoritative = mapper.ToDto(db, entity);
    mapper.AssignTemporaryKeys(db, applied, current, authoritative);

    logger.LogInformation(
        "Updated order {OrderId}; {LineCount} lines persisted",
        id,
        authoritative.Lines.Count
    );
    return Results.Ok(authoritative);
}
