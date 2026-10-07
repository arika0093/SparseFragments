using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CollaborativeEditing.Contracts;
using Shouldly;

namespace CollaborativeEditing.Tests;

internal static class CollabHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static async Task<Snapshot> GetSnapshotAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/workspaces/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull();
        var snapshot = await ReadSnapshotAsync(response);
        snapshot.ShouldNotBeNull();
        response.Headers.ETag!.Tag.ShouldBe($"\"{snapshot!.Revision}\"");
        return snapshot;
    }

    internal static async Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        Guid id,
        long revision,
        ReadOnlyMemory<byte> jsonPatch
    )
    {
        using var request = new HttpRequestMessage(
            new HttpMethod("PATCH"),
            $"/api/workspaces/{id}"
        );
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{revision}\""));
        request.Content = new ByteArrayContent(jsonPatch.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json-patch+json"
        );
        return await client.SendAsync(request);
    }

    internal static async Task<Snapshot?> ReadSnapshotAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (
            !root.TryGetProperty("workspace", out var workspaceEl)
            || !root.TryGetProperty("revision", out var revisionEl)
        )
        {
            return null;
        }

        var id = workspaceEl.GetProperty("id").GetGuid();
        var name = workspaceEl.GetProperty("name").GetString() ?? string.Empty;
        var settings =
            JsonSerializer.Deserialize<WorkspaceSettings>(
                workspaceEl.GetProperty("settings").GetRawText(),
                JsonOptions
            ) ?? new WorkspaceSettings();
        var quests =
            JsonSerializer.Deserialize<List<Quest>>(
                workspaceEl.GetProperty("quests").GetRawText(),
                JsonOptions
            ) ?? new List<Quest>();
        var effective = root.TryGetProperty("effectiveSettings", out var effEl)
            ? JsonSerializer.Deserialize<WorkspaceSettings>(effEl.GetRawText(), JsonOptions)
                ?? new WorkspaceSettings()
            : new WorkspaceSettings();
        return new Snapshot(id, name, settings, quests, effective, revisionEl.GetInt64());
    }
}

internal sealed record Snapshot(
    Guid Id,
    string Name,
    WorkspaceSettings Settings,
    List<Quest> Quests,
    WorkspaceSettings EffectiveSettings,
    long Revision
)
{
    public Workspace ToWorkspace() =>
        new()
        {
            Name = Name,
            Settings = new WorkspaceSettings
            {
                Theme = Settings.Theme,
                RetryCount = Settings.RetryCount,
                Notifications = Settings.Notifications,
            },
            Quests = Quests
                .Select(q => new Quest
                {
                    Id = q.Id,
                    Title = q.Title,
                    Points = q.Points,
                    Scores = new List<int>(q.Scores ?? new List<int>()),
                })
                .ToList(),
        };
}
