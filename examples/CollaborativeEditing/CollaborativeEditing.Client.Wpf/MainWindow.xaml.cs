using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using CollaborativeEditing.Contracts;
using SparseFragments;

namespace CollaborativeEditing.Client.Wpf;

/// <summary>
/// Plain code-behind session over the shared <see cref="Workspace"/> model.
/// Edits mutate the live model through the generated
/// <c>Workspace.Observable</c> wrapper; Save retains the baseline
/// <c>Workspace.Fragment</c>, derives <c>Workspace.Patch.Between</c>,
/// exports <c>ToJsonPatch</c>, PATCHes with <c>If-Match</c>, and on 412
/// runs <c>Rebase(base, local, current)</c> (auto-retry when clean,
/// otherwise lists Path/PathText/kind/values conflicts).
/// </summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private Workspace _model = new();
    private Workspace.Observable _observable;
    private Workspace.Fragment _baseline = Workspace.Fragment.From(new Workspace());
    private long _revision;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        _observable = new Workspace.Observable(_model, OnModelChanged);
        DataContext = _observable;
        RefreshQuestList();
        UpdateStatus("Load a workspace to begin.", "idle");
    }

    private void OnModelChanged()
    {
        if (_loading)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            DirtyText.Text = "edited — Save to push";
            RefreshQuestList(keepSelection: true);
        });
    }

    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await SaveAsync();
    }

    private async Task LoadAsync()
    {
        var baseUrl = ServerBox.Text.Trim();
        if (!Guid.TryParse(WorkspaceBox.Text.Trim(), out var workspaceId))
        {
            UpdateStatus("Workspace id must be a GUID.", "load failed");
            return;
        }

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
            var response = await http.GetAsync($"api/workspaces/{workspaceId}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                UpdateStatus("Workspace not found (404).", "load failed");
                return;
            }

            response.EnsureSuccessStatusCode();
            var snapshot = await ReadSnapshotAsync(response);
            if (snapshot is null)
            {
                UpdateStatus("Could not parse workspace payload.", "load failed");
                return;
            }

            _loading = true;
            try
            {
                _model = snapshot.ToWorkspace();
                _baseline = Workspace.Fragment.From(_model);
                _revision = snapshot.Revision;
                _observable = new Workspace.Observable(_model, OnModelChanged);
                DataContext = _observable;
                ConflictList.ItemsSource = Array.Empty<string>();
                QuestTitleBox.Text = string.Empty;
                QuestPointsBox.Text = string.Empty;
                RefreshQuestList();
                DirtyText.Text = string.Empty;
            }
            finally
            {
                _loading = false;
            }

            var etag = response.Headers.ETag?.Tag ?? $"\"{_revision}\"";
            UpdateStatus($"Loaded revision {_revision} (ETag {etag}).", "loaded");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Load failed: {ex.Message}", "load failed");
        }
    }

    private async Task SaveAsync()
    {
        var baseUrl = ServerBox.Text.Trim();
        if (!Guid.TryParse(WorkspaceBox.Text.Trim(), out var workspaceId))
        {
            UpdateStatus("Workspace id must be a GUID.", "save failed");
            return;
        }

        Workspace.Fragment currentFragment;
        Workspace.Patch localPatch;
        try
        {
            currentFragment = Workspace.Fragment.From(_model);
            var baseOpt = Optional<Workspace.Fragment?>.Present(_baseline);
            var currentOpt = Optional<Workspace.Fragment?>.Present(currentFragment);
            localPatch = Workspace.Patch.Between(baseOpt, currentOpt);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Could not diff local edits: {ex.Message}", "diff failed");
            return;
        }

        if (localPatch.IsEmpty)
        {
            UpdateStatus($"No changes (revision {_revision}).", "clean");
            return;
        }

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
            var baseOpt = Optional<Workspace.Fragment?>.Present(_baseline);
            var jsonPatch = localPatch.ToJsonPatch(baseOpt);

            var response = await SendPatchAsync(http, workspaceId, _revision, jsonPatch);
            if (response.IsSuccessStatusCode)
            {
                var snapshot = await ReadSnapshotAsync(response);
                if (snapshot is not null)
                {
                    ApplyServerSnapshot(snapshot);
                    UpdateStatus($"Saved revision {_revision}.", "saved");
                }
                else
                {
                    _baseline = currentFragment;
                    UpdateStatus($"Saved (could not parse body) revision {_revision}.", "saved");
                }

                return;
            }

            if ((int)response.StatusCode != 412)
            {
                var detail = await response.Content.ReadAsStringAsync();
                UpdateStatus(
                    $"Save failed {(int)response.StatusCode}: {Trim(detail, 220)}",
                    "save failed"
                );
                return;
            }

            // 412: rebase onto latest, auto-retry when clean, otherwise surface conflicts.
            var latest = await ReadSnapshotAsync(response);
            if (latest is null)
            {
                UpdateStatus(
                    "Stale revision (412) but latest state was unreadable.",
                    "rebase failed"
                );
                return;
            }

            var latestModel = latest.ToWorkspace();
            var latestFragment = Workspace.Fragment.From(latestModel);
            var currentOpt = Optional<Workspace.Fragment?>.Present(currentFragment);
            var latestOpt = Optional<Workspace.Fragment?>.Present(latestFragment);
            var rebase = Workspace.Patch.Rebase(baseOpt, localPatch, latestOpt);

            if (!rebase.HasConflicts)
            {
                var rebasedJson = rebase.Patch.ToJsonPatch(latestOpt);
                var retry = await SendPatchAsync(http, workspaceId, latest.Revision, rebasedJson);
                if (retry.IsSuccessStatusCode)
                {
                    var snapshot = await ReadSnapshotAsync(retry);
                    if (snapshot is not null)
                    {
                        ApplyServerSnapshot(snapshot);
                    }
                    else
                    {
                        _baseline = rebase.Patch.Apply(latestOpt).Value ?? latestFragment;
                        _revision = latest.Revision + 1;
                        RefreshObservable();
                    }

                    UpdateStatus(
                        $"Rebased onto {latest.Revision}, saved {_revision}.",
                        "rebased + saved"
                    );
                }
                else
                {
                    var detail = await retry.Content.ReadAsStringAsync();
                    UpdateStatus(
                        $"Rebase retry failed {(int)retry.StatusCode}: {Trim(detail, 220)}",
                        "rebase failed"
                    );
                }

                return;
            }

            // Conflicting: keep the merged (non-conflicting) state, list the rest.
            var merged = rebase.Patch.Apply(latestOpt).Value ?? latestFragment;
            _model = merged.ToModel();
            _baseline = latestFragment;
            _revision = latest.Revision;
            RefreshObservable();
            ShowConflicts(rebase.Conflicts);
            UpdateStatus(
                $"Revision {_revision}: {rebase.Conflicts.Count} conflict(s), merged the rest.",
                "conflicts"
            );
        }
        catch (Exception ex)
        {
            UpdateStatus($"Save failed: {ex.Message}", "save failed");
        }
    }

    private static async Task<HttpResponseMessage> SendPatchAsync(
        HttpClient http,
        Guid workspaceId,
        long revision,
        ReadOnlyMemory<byte> jsonPatch
    )
    {
        using var request = new HttpRequestMessage(
            new HttpMethod("PATCH"),
            $"api/workspaces/{workspaceId}"
        );
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{revision}\""));
        request.Content = new ByteArrayContent(jsonPatch.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json-patch+json"
        );
        return await http.SendAsync(request);
    }

    private void ApplyServerSnapshot(Snapshot snapshot)
    {
        _loading = true;
        try
        {
            _model = snapshot.ToWorkspace();
            _baseline = Workspace.Fragment.From(_model);
            _revision = snapshot.Revision;
            _observable = new Workspace.Observable(_model, OnModelChanged);
            DataContext = _observable;
            ConflictList.ItemsSource = Array.Empty<string>();
            RefreshQuestList();
            DirtyText.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshObservable()
    {
        _loading = true;
        try
        {
            _observable = new Workspace.Observable(_model, OnModelChanged);
            DataContext = _observable;
            RefreshQuestList();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowConflicts(IReadOnlyList<SparsePatchConflict> conflicts)
    {
        ConflictList.ItemsSource = conflicts
            .Select(c =>
                $"{c.PathText} [{c.Kind}] base={FormatOptional(c.BaseValue)} local={FormatOptional(c.LocalValue)} current={FormatOptional(c.CurrentValue)}"
            )
            .ToList();
    }

    private static string FormatOptional(Optional<object?> value) =>
        !value.IsPresent ? "(absent)" : value.Value?.ToString() ?? "null";

    private void UpdateStatus(string revisionLine, string outcome)
    {
        RevisionText.Text = $"revision: {_revision} — {revisionLine}";
        RebaseText.Text = outcome;
    }

    private static async Task<Snapshot?> ReadSnapshotAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var workspaceEl = root.GetProperty("workspace");
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
            var revision = root.GetProperty("revision").GetInt64();
            var etag = response.Headers.ETag?.Tag;
            return new Snapshot(id, name, settings, quests, effective, revision, etag);
        }
        catch
        {
            return null;
        }
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max) + "…";

    #region Quests (replace-only list helpers)

    private Quest? SelectedQuest() => QuestList.SelectedItem as Quest;

    private void RefreshQuestList(bool keepSelection = false)
    {
        var selectedId = keepSelection ? SelectedQuest()?.Id : null;
        QuestList.ItemsSource = null;
        QuestList.ItemsSource = _model.Quests;
        if (selectedId is not null)
        {
            QuestList.SelectedItem = _model.Quests.FirstOrDefault(q => q.Id == selectedId);
        }
    }

    private void SyncQuestsChanged()
    {
        // Observable collections are replace-only for notification purposes:
        // publish a new list instance so bindings observe the change.
        _observable.Quests = new List<Quest>(_model.Quests);
        DirtyText.Text = "edited — Save to push";
        RefreshQuestList(keepSelection: true);
    }

    private void OnQuestSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e
    )
    {
        var quest = SelectedQuest();
        QuestTitleBox.Text = quest?.Title ?? string.Empty;
        QuestPointsBox.Text = quest is null ? string.Empty : quest.Points.ToString();
    }

    private void OnQuestAdd(object sender, RoutedEventArgs e)
    {
        var quest = new Quest
        {
            Id = $"q-{Guid.NewGuid().ToString("N").Substring(0, 6)}",
            Title = "New quest",
            Points = 0,
            Scores = new List<int>(),
        };
        _model.Quests.Add(quest);
        SyncQuestsChanged();
        QuestList.SelectedItem = quest;
    }

    private void OnQuestRemove(object sender, RoutedEventArgs e)
    {
        var quest = SelectedQuest();
        if (quest is null)
        {
            return;
        }

        _model.Quests.RemoveAll(q => q.Id == quest.Id);
        SyncQuestsChanged();
    }

    private void OnQuestUp(object sender, RoutedEventArgs e)
    {
        var quest = SelectedQuest();
        if (quest is null)
        {
            return;
        }

        var index = _model.Quests.FindIndex(q => q.Id == quest.Id);
        if (index > 0)
        {
            (_model.Quests[index - 1], _model.Quests[index]) = (
                _model.Quests[index],
                _model.Quests[index - 1]
            );
            SyncQuestsChanged();
            QuestList.SelectedItem = quest;
        }
    }

    private void OnQuestDown(object sender, RoutedEventArgs e)
    {
        var quest = SelectedQuest();
        if (quest is null)
        {
            return;
        }

        var index = _model.Quests.FindIndex(q => q.Id == quest.Id);
        if (index >= 0 && index < _model.Quests.Count - 1)
        {
            (_model.Quests[index + 1], _model.Quests[index]) = (
                _model.Quests[index],
                _model.Quests[index + 1]
            );
            SyncQuestsChanged();
            QuestList.SelectedItem = quest;
        }
    }

    private void OnQuestTitleChanged(object sender, RoutedEventArgs e)
    {
        var quest = SelectedQuest();
        if (quest is null)
        {
            return;
        }

        var target = _model.Quests.FirstOrDefault(q => q.Id == quest.Id);
        if (target is not null && target.Title != QuestTitleBox.Text)
        {
            target.Title = QuestTitleBox.Text;
            SyncQuestsChanged();
            QuestList.SelectedItem = target;
        }
    }

    private void OnQuestPointsChanged(object sender, RoutedEventArgs e)
    {
        var quest = SelectedQuest();
        if (quest is null)
        {
            return;
        }

        if (int.TryParse(QuestPointsBox.Text, out var points))
        {
            var target = _model.Quests.FirstOrDefault(q => q.Id == quest.Id);
            if (target is not null && target.Points != points)
            {
                target.Points = points;
                SyncQuestsChanged();
                QuestList.SelectedItem = target;
            }
        }
    }

    #endregion

    private sealed record Snapshot(
        Guid Id,
        string Name,
        WorkspaceSettings Settings,
        List<Quest> Quests,
        WorkspaceSettings EffectiveSettings,
        long Revision,
        string? ETag
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
}
