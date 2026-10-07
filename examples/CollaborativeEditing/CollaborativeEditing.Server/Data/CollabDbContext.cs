using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace CollaborativeEditing.Server.Data;

/// <summary>Workspace aggregate row. Plain entity, intentionally NOT a SparseFragmentModel.</summary>
public sealed class WorkspaceEntity
{
    public Guid Id { get; set; }

    [ConcurrencyCheck]
    public long Revision { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Theme { get; set; } = "light";

    public int RetryCount { get; set; } = 3;

    public bool Notifications { get; set; } = true;

    public List<QuestEntity> Quests { get; set; } = new();
}

/// <summary>Quest row keyed by stable <see cref="QuestEntity.KeyId"/> within a workspace.</summary>
public sealed class QuestEntity
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public WorkspaceEntity? Workspace { get; set; }

    public string KeyId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Points { get; set; }

    public string ScoresJson { get; set; } = "[]";

    public int Order { get; set; }
}

/// <summary>EF Core store for the collaborative-editing demo.</summary>
public sealed class CollabDbContext : DbContext
{
    /// <summary>Stable seed workspace id ("Demo"). Clients can GET this id directly.</summary>
    public static readonly Guid SeedWorkspaceId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111"
    );

    public CollabDbContext(DbContextOptions<CollabDbContext> options)
        : base(options) { }

    public DbSet<WorkspaceEntity> Workspaces => Set<WorkspaceEntity>();

    public DbSet<QuestEntity> Quests => Set<QuestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkspaceEntity>(b =>
        {
            b.HasKey(w => w.Id);
            b.Property(w => w.Revision).IsConcurrencyToken();
            b.Property(w => w.Name).IsRequired();
            b.HasMany(w => w.Quests)
                .WithOne(q => q.Workspace)
                .HasForeignKey(q => q.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuestEntity>(b =>
        {
            b.HasKey(q => q.Id);
            b.Property(q => q.KeyId).IsRequired();
            b.Property(q => q.Title).IsRequired();
            b.Property(q => q.ScoresJson).IsRequired();
            b.HasIndex(q => new { q.WorkspaceId, q.KeyId }).IsUnique();
        });
    }

    /// <summary>Seeds the single "Demo" workspace when the store is empty.</summary>
    public static void EnsureSeeded(CollabDbContext db)
    {
        if (db.Workspaces.Any())
        {
            return;
        }

        var workspace = new WorkspaceEntity
        {
            Id = SeedWorkspaceId,
            Revision = 1,
            Name = "Demo",
            Theme = "dark",
            RetryCount = 5,
            Notifications = false,
            Quests = new List<QuestEntity>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = SeedWorkspaceId,
                    KeyId = "a",
                    Title = "First quest",
                    Points = 10,
                    ScoresJson = "[1,2]",
                    Order = 0,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = SeedWorkspaceId,
                    KeyId = "b",
                    Title = "Second quest",
                    Points = 20,
                    ScoresJson = "[3]",
                    Order = 1,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = SeedWorkspaceId,
                    KeyId = "c",
                    Title = "Third quest",
                    Points = 30,
                    ScoresJson = "[]",
                    Order = 2,
                },
            },
        };

        db.Workspaces.Add(workspace);
        db.SaveChanges();
    }
}
