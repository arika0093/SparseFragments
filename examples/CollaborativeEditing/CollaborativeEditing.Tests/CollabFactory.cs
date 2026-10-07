using CollaborativeEditing.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CollaborativeEditing.Tests;

/// <summary>
/// Isolated demo server: fresh InMemory store per factory so tests never share revisions.
/// </summary>
public sealed class CollabFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "collab-" + Guid.NewGuid().ToString("N");

    public CollabFactory()
    {
        Environment.SetEnvironmentVariable("COLLAB_USE_INMEMORY", "1");
    }

    public Guid SeedId => CollabDbContext.SeedWorkspaceId;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(static d =>
                    d.ServiceType == typeof(DbContextOptions<CollabDbContext>)
                    || d.ServiceType == typeof(DbContextOptions)
                    || d.ServiceType == typeof(CollabDbContext)
                )
                .ToList();
            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<CollabDbContext>(options => options.UseInMemoryDatabase(_dbName));
        });
    }

    /// <summary>
    /// Creates a client and resets this factory's isolated store to the seed state,
    /// so each test starts from revision 1 even when tests run sequentially.
    /// </summary>
    public HttpClient CreateSeededClient()
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
        CollabDbContext.EnsureSeeded(db);
        return client;
    }
}
