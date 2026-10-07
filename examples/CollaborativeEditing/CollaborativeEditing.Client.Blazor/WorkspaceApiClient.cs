using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CollaborativeEditing.Contracts;

namespace CollaborativeEditing.Client.Blazor;

// Snapshot returned by GET and by successful/conflicted PATCH responses:
// the canonical workspace, the server-computed effective settings, the
// aggregate revision, and the ETag used for If-Match concurrency control.
public sealed record LoadedSnapshot(
    Workspace Workspace,
    WorkspaceSettings EffectiveSettings,
    long Revision,
    string? ETag
);

// Outcomes of PATCH /api/workspaces/{id}.
public abstract record SaveOutcome;

public sealed record Saved(LoadedSnapshot Snapshot) : SaveOutcome;

public sealed record SaveConflict(LoadedSnapshot Latest) : SaveOutcome;

public sealed record SaveRejected(string Detail) : SaveOutcome;

// Minimal HTTP client for the CollaborativeEditing server API implemented in
// parallel (GET → snapshot + ETag; PATCH with If-Match + JSON Patch →
// 200 updated / 412 latest snapshot / 400 ProblemDetails / 404).
public sealed class WorkspaceApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http = http;

    // When set, requests target this base URL instead of HttpClient.BaseAddress.
    // Useful when the API runs on a different origin than the Blazor app.
    public string? BaseUrlOverride { get; set; }

    public async Task<LoadedSnapshot> GetAsync(
        string id,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var response = await _http.GetAsync(
            Url(id),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Workspace '{id}' was not found (404).");
        }

        response.EnsureSuccessStatusCode();
        return await ReadSnapshotAsync(response, cancellationToken);
    }

    public async Task<SaveOutcome> PatchAsync(
        string id,
        string? etag,
        ReadOnlyMemory<byte> jsonPatch,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var request = new HttpRequestMessage(HttpMethod.Patch, Url(id));
        request.Content = new ByteArrayContent(jsonPatch.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json-patch+json"
        );
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // 412 carries the latest snapshot so the caller can rebase.
            return new SaveConflict(await ReadSnapshotAsync(response, cancellationToken));
        }

        if (response.IsSuccessStatusCode)
        {
            return new Saved(await ReadSnapshotAsync(response, cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new SaveRejected($"Workspace '{id}' was not found (404).");
        }

        return new SaveRejected(await ReadProblemDetailAsync(response, cancellationToken));
    }

    private string Url(string id)
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrlOverride)
            ? _http.BaseAddress?.ToString()
            : BaseUrlOverride.TrimEnd('/') + "/";
        return $"{baseUrl}api/workspaces/{Uri.EscapeDataString(id)}";
    }

    private static async Task<LoadedSnapshot> ReadSnapshotAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        var dto = await response.Content.ReadFromJsonAsync<SnapshotDto>(Json, cancellationToken);
        ArgumentNullException.ThrowIfNull(dto?.Workspace);
        return new LoadedSnapshot(
            dto.Workspace,
            dto.EffectiveSettings ?? new WorkspaceSettings(),
            dto.Revision,
            response.Headers.ETag?.ToString()
        );
    }

    private static async Task<string> ReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDto>(
                Json,
                cancellationToken
            );
            var detail = string.IsNullOrWhiteSpace(problem?.Detail)
                ? problem?.Title
                : $"{problem?.Title}: {problem?.Detail}";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return $"Request failed ({(int)response.StatusCode}): {detail}";
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Non-JSON error bodies (for example routing 404 pages) fall
            // through to the status-based message below.
        }

        return $"Request failed with {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    private sealed record SnapshotDto(
        Workspace? Workspace,
        WorkspaceSettings? EffectiveSettings,
        long Revision
    );

    private sealed record ProblemDto(string? Title, string? Detail);
}
