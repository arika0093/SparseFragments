using CollaborativeEditing.Contracts;
using CollaborativeEditing.Server;
using CollaborativeEditing.Server.Data;
using Microsoft.EntityFrameworkCore;
using SparseFragments;

var webApplicationBuilder = WebApplication.CreateBuilder(args);

var useInMemory = string.Equals(
    Environment.GetEnvironmentVariable("COLLAB_USE_INMEMORY"),
    "1",
    StringComparison.Ordinal
);
var connectionString =
    webApplicationBuilder.Configuration.GetConnectionString("collabdb")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__collabdb");

webApplicationBuilder.Services.AddDbContext<CollabDbContext>(options =>
{
    if (useInMemory || string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseInMemoryDatabase("collabdb");
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});

var webApplication = webApplicationBuilder.Build();

using (var scope = webApplication.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CollabDbContext>();
    db.Database.EnsureCreated();
    CollabDbContext.EnsureSeeded(db);
}

webApplication.MapGet(
    "/api/workspaces/{id:guid}",
    async (Guid id, CollabDbContext db, HttpContext http) =>
    {
        var entity = await db
            .Workspaces.Include(w => w.Quests)
            .FirstOrDefaultAsync(w => w.Id == id);
        return entity is null
            ? Results.Problem(statusCode: 404, title: "Workspace not found.")
            : WorkspacePayload(http, entity);
    }
);

webApplication.MapPatch(
    "/api/workspaces/{id:guid}",
    async (Guid id, HttpContext http, CollabDbContext db, ILogger<Program> logger) =>
    {
        var ifMatchRaw = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatchRaw))
        {
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} quests={QuestCount}",
                "invalid",
                id,
                0
            );
            return Results.Problem(
                "Send If-Match: \"<revision>\" with the workspace revision.",
                statusCode: 428,
                title: "If-Match header is required."
            );
        }

        if (!TryParseRevision(ifMatchRaw, out var ifMatch))
        {
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} quests={QuestCount}",
                "invalid",
                id,
                -1,
                0
            );
            return Results.Problem(
                $"Could not parse revision from If-Match '{ifMatchRaw}'.",
                statusCode: 400,
                title: "Invalid If-Match header."
            );
        }

        var entity = await db
            .Workspaces.Include(w => w.Quests)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (entity is null)
        {
            return Results.Problem(statusCode: 404, title: "Workspace not found.");
        }

        if (entity.Revision != ifMatch)
        {
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} newRevision={NewRevision} quests={QuestCount}",
                "stale",
                entity.Id,
                entity.Revision,
                ifMatch,
                entity.Quests.Count
            );
            http.Response.Headers.ETag = $"\"{entity.Revision}\"";
            return Results.Json(
                CurrentState(entity),
                options: null,
                contentType: null,
                statusCode: 412
            );
        }

        var contentType = http.Request.ContentType ?? string.Empty;
        if (
            !contentType.StartsWith(
                "application/json-patch+json",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} quests={QuestCount}",
                "invalid",
                entity.Id,
                entity.Revision,
                entity.Quests.Count
            );
            return Results.Problem(
                "Content-Type must be application/json-patch+json (RFC 6902).",
                statusCode: 415,
                title: "Unsupported Media Type."
            );
        }

        byte[] patchBytes;
        using (var body = new MemoryStream())
        {
            await http.Request.Body.CopyToAsync(body);
            patchBytes = body.ToArray();
        }

        if (patchBytes.Length == 0)
        {
            return Results.Problem(
                "The RFC 6902 patch array must not be empty.",
                statusCode: 400,
                title: "Empty patch document."
            );
        }

        Workspace.Fragment baselineFragment;
        try
        {
            baselineFragment = Workspace.Fragment.From(WorkspaceMapper.ToModel(entity));
        }
        catch (Exception ex)
        {
            return Results.Problem(
                ex.Message,
                statusCode: 500,
                title: "Failed to snapshot workspace."
            );
        }

        Workspace.Patch patch;
        try
        {
            patch = Workspace.Patch.FromJsonPatch(
                Optional<Workspace.Fragment?>.Present(baselineFragment),
                (ReadOnlyMemory<byte>)patchBytes,
                null
            );
        }
        catch (JsonPatchException ex)
        {
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} quests={QuestCount} error={Error}",
                "invalid",
                entity.Id,
                entity.Revision,
                entity.Quests.Count,
                ex.Kind.ToString()
            );
            return Results.Problem(
                $"[{ex.Kind}] {ex.Message}",
                statusCode: 400,
                title: "Invalid JSON Patch document."
            );
        }

        Workspace updated;
        try
        {
            updated = baselineFragment.Apply(patch).ToModel();
        }
        catch (Exception ex)
        {
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} quests={QuestCount} error={Error}",
                "invalid",
                entity.Id,
                entity.Revision,
                entity.Quests.Count,
                ex.Message
            );
            return Results.Problem(
                ex.Message,
                statusCode: 400,
                title: "Patch could not be applied."
            );
        }

        var errors = WorkspaceMapper.Validate(updated);
        if (errors.Count > 0)
        {
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} quests={QuestCount} errors={Errors}",
                "invalid",
                entity.Id,
                entity.Revision,
                entity.Quests.Count,
                string.Join("; ", errors)
            );
            return Results.Problem(
                string.Join(" ", errors),
                statusCode: 400,
                title: "Workspace validation failed."
            );
        }

        var currentRevision = entity.Revision;
        WorkspaceMapper.ApplyToEntity(db, entity, updated);
        entity.Revision = currentRevision + 1;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(entity).ReloadAsync();
            var current = await db.Workspaces.Include(w => w.Quests).FirstAsync(w => w.Id == id);
            logger.LogInformation(
                "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} newRevision={NewRevision} quests={QuestCount}",
                "stale",
                current.Id,
                current.Revision,
                ifMatch,
                current.Quests.Count
            );
            http.Response.Headers.ETag = $"\"{current.Revision}\"";
            return Results.Json(
                CurrentState(current),
                options: null,
                contentType: null,
                statusCode: 412
            );
        }

        logger.LogInformation(
            "Workspace patch {Outcome} {WorkspaceId} currentRevision={CurrentRevision} newRevision={NewRevision} quests={QuestCount}",
            "applied",
            entity.Id,
            currentRevision,
            entity.Revision,
            entity.Quests.Count
        );
        return WorkspacePayload(http, entity);
    }
);

webApplication.MapGet(
    "/",
    () =>
        Results.Json(
            new
            {
                seedWorkspaceId = CollabDbContext.SeedWorkspaceId,
                systemDefaults = SystemDefaults.Value,
            }
        )
);

webApplication.Run();

static object CurrentState(WorkspaceEntity current)
{
    var model = WorkspaceMapper.ToModel(current);
    return new
    {
        workspace = new
        {
            id = current.Id,
            name = model.Name,
            settings = model.Settings,
            quests = model.Quests,
        },
        effectiveSettings = SystemDefaults.Effective(model.Settings),
        revision = current.Revision,
    };
}

static bool TryParseRevision(string raw, out long revision)
{
    revision = 0;
    var text = raw.Trim();
    if (text.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
    {
        text = text.Substring(2).Trim();
    }

    text = text.Trim().Trim('"').Trim();
    if (text == "*" || text.Length == 0)
    {
        return false;
    }

    var token = text.Split(',')[0].Trim().Trim('"').Trim();
    return long.TryParse(token, out revision);
}

static IResult WorkspacePayload(HttpContext http, WorkspaceEntity entity)
{
    var model = WorkspaceMapper.ToModel(entity);
    var effectiveSettings = SystemDefaults.Effective(model.Settings);
    http.Response.Headers.ETag = $"\"{entity.Revision}\"";
    return Results.Json(
        new
        {
            workspace = new
            {
                id = entity.Id,
                name = model.Name,
                settings = model.Settings,
                quests = model.Quests,
            },
            effectiveSettings,
            revision = entity.Revision,
        }
    );
}
