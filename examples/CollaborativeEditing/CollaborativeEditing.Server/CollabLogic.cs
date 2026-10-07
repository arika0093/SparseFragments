using System.Text.Json;
using CollaborativeEditing.Contracts;
using CollaborativeEditing.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CollaborativeEditing.Server;

/// <summary>System-level settings defaults. The stored workspace settings are an override layer.</summary>
public static class SystemDefaults
{
    public static WorkspaceSettings Value { get; } =
        new()
        {
            Theme = "light",
            RetryCount = 3,
            Notifications = true,
        };

    /// <summary>
    /// Computes effective settings via Fragment layering:
    /// <c>SystemDefaultsFragment.Merge(overrideFragment).ToModel()</c>.
    /// </summary>
    public static WorkspaceSettings Effective(WorkspaceSettings overrides)
    {
        var defaultsFragment = WorkspaceSettings.Fragment.From(Value);
        var overrideFragment = WorkspaceSettings.Fragment.From(overrides);
        return defaultsFragment.Merge(overrideFragment).ToModel();
    }
}

/// <summary>Entity &lt;-&gt; contract-model mapper. Quests ordered by <c>Order</c>.</summary>
public static class WorkspaceMapper
{
    public static Workspace ToModel(WorkspaceEntity entity)
    {
        return new Workspace
        {
            Name = entity.Name,
            Settings = new WorkspaceSettings
            {
                Theme = entity.Theme,
                RetryCount = entity.RetryCount,
                Notifications = entity.Notifications,
            },
            Quests = entity
                .Quests.OrderBy(q => q.Order)
                .Select(q => new Quest
                {
                    Id = q.KeyId,
                    Title = q.Title,
                    Points = q.Points,
                    Scores = DeserializeScores(q.ScoresJson),
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Applies an updated contract model onto a tracked entity (Revision untouched).
    /// New rows are marked <see cref="EntityState.Added"/> explicitly: they carry
    /// assigned keys, which change-tracker discovery would otherwise read as
    /// pre-existing rows and mark <see cref="EntityState.Modified"/>.
    /// </summary>
    public static void ApplyToEntity(CollabDbContext db, WorkspaceEntity entity, Workspace model)
    {
        entity.Name = model.Name;
        entity.Theme = model.Settings.Theme;
        entity.RetryCount = model.Settings.RetryCount;
        entity.Notifications = model.Settings.Notifications;

        var incoming = model.Quests ?? new List<Quest>();
        var existing = entity.Quests.ToDictionary(q => q.KeyId, StringComparer.Ordinal);

        var synced = new List<QuestEntity>(incoming.Count);
        var added = new List<QuestEntity>();
        for (var i = 0; i < incoming.Count; i++)
        {
            var quest = incoming[i];
            if (existing.TryGetValue(quest.Id, out var row))
            {
                row.Title = quest.Title;
                row.Points = quest.Points;
                row.ScoresJson = JsonSerializer.Serialize(quest.Scores ?? new List<int>());
                row.Order = i;
                existing.Remove(quest.Id);
                synced.Add(row);
            }
            else
            {
                var fresh = new QuestEntity
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = entity.Id,
                    KeyId = quest.Id,
                    Title = quest.Title,
                    Points = quest.Points,
                    ScoresJson = JsonSerializer.Serialize(quest.Scores ?? new List<int>()),
                    Order = i,
                };
                synced.Add(fresh);
                added.Add(fresh);
            }
        }

        foreach (var removed in existing.Values)
        {
            entity.Quests.Remove(removed);
            db.Remove(removed);
        }

        foreach (var row in synced)
        {
            if (!entity.Quests.Contains(row))
            {
                entity.Quests.Add(row);
            }
        }

        foreach (var row in added)
        {
            db.Entry(row).State = EntityState.Added;
        }
    }

    public static List<int> DeserializeScores(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
        }
        catch (JsonException)
        {
            return new List<int>();
        }
    }

    /// <summary>Validates a patched model. Returns human-readable errors (empty = valid).</summary>
    public static List<string> Validate(Workspace? model)
    {
        var errors = new List<string>();
        if (model is null)
        {
            errors.Add("workspace must be an object.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            errors.Add("Name must be non-empty.");
        }

        if (model.Settings is null)
        {
            errors.Add("Settings must be present.");
        }

        if (model.Quests is null)
        {
            errors.Add("Quests must be present.");
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var quest in model.Quests)
        {
            if (string.IsNullOrWhiteSpace(quest.Id))
            {
                errors.Add("Quest Id must be non-empty.");
            }
            else if (!seen.Add(quest.Id))
            {
                errors.Add($"Duplicate quest key '{quest.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(quest.Title))
            {
                errors.Add($"Quest '{quest.Id}' Title must be non-empty.");
            }

            if (quest.Points < 0)
            {
                errors.Add($"Quest '{quest.Id}' Points must be >= 0.");
            }
        }

        return errors;
    }
}
