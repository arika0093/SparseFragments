using System.Text;
using System.Text.Json;
using SparseFragments;
using SparseFragments.Playground.Models;

namespace SparseFragments.Playground.Models;

/// <summary>Presence selector for nullable fragment members.</summary>
public enum NullPresence
{
    /// <summary>Member is absent and falls through to lower layers.</summary>
    Missing,

    /// <summary>Member is explicitly null and overrides lower layers.</summary>
    Null,

    /// <summary>Member carries a value.</summary>
    Value,
}

/// <summary>Mutation selector for non-nullable patch members.</summary>
public enum PatchKind
{
    /// <summary>Leave the member unchanged.</summary>
    Unchanged,

    /// <summary>Set the member to the edited value.</summary>
    Set,

    /// <summary>Remove the member contribution.</summary>
    Unset,
}

/// <summary>Mutation selector for nullable patch members.</summary>
public enum NullPatchKind
{
    /// <summary>Leave the member unchanged.</summary>
    Unchanged,

    /// <summary>Set the member to the edited value.</summary>
    Set,

    /// <summary>Set the member to an explicit null.</summary>
    SetNull,

    /// <summary>Remove the member contribution.</summary>
    Unset,
}

/// <summary>Mutation selector for the nested patch member.</summary>
public enum NestedPatchKind
{
    /// <summary>Leave the nested member unchanged.</summary>
    Unchanged,

    /// <summary>Set the nested member to an explicit null.</summary>
    SetNull,

    /// <summary>Remove the nested member contribution.</summary>
    Unset,

    /// <summary>Edit nested members below.</summary>
    Set,
}

/// <summary>Editable tri-state values for one <see cref="PlaygroundSettings.Fragment"/>.</summary>
public sealed class FragmentEditState
{
    /// <summary>Gets or sets whether Enabled is present.</summary>
    public bool EnabledPresent { get; set; }

    /// <summary>Gets or sets the Enabled value.</summary>
    public bool EnabledValue { get; set; } = true;

    /// <summary>Gets or sets whether RetryCount is present.</summary>
    public bool RetryPresent { get; set; }

    /// <summary>Gets or sets the RetryCount value.</summary>
    public int RetryValue { get; set; } = 3;

    /// <summary>Gets or sets the Label presence.</summary>
    public NullPresence LabelMode { get; set; }

    /// <summary>Gets or sets the Label value.</summary>
    public string LabelValue { get; set; } = string.Empty;

    /// <summary>Gets or sets the Nested presence.</summary>
    public NullPresence NestedMode { get; set; }

    /// <summary>Gets or sets whether Nested.Host is present.</summary>
    public bool HostPresent { get; set; }

    /// <summary>Gets or sets the Nested.Host value.</summary>
    public string HostValue { get; set; } = string.Empty;

    /// <summary>Gets or sets whether Nested.Port is present.</summary>
    public bool PortPresent { get; set; }

    /// <summary>Gets or sets the Nested.Port value.</summary>
    public int PortValue { get; set; } = 5432;

    /// <summary>Gets or sets whether Plugins is present.</summary>
    public bool PluginsPresent { get; set; }

    /// <summary>Gets or sets comma-separated plugin names.</summary>
    public string PluginsText { get; set; } = string.Empty;

    /// <summary>Creates the default lower (baseline) layer.</summary>
    public static FragmentEditState LowerDefaults() =>
        new()
        {
            EnabledPresent = true,
            EnabledValue = true,
            RetryPresent = true,
            RetryValue = 3,
            LabelMode = NullPresence.Value,
            LabelValue = "default",
            NestedMode = NullPresence.Value,
            HostPresent = true,
            HostValue = "localhost",
            PortPresent = true,
            PortValue = 5432,
            PluginsPresent = true,
            PluginsText = "base-plugin",
        };

    /// <summary>Creates the default higher (overlay) layer.</summary>
    public static FragmentEditState HigherDefaults() =>
        new()
        {
            EnabledPresent = false,
            EnabledValue = false,
            RetryPresent = false,
            RetryValue = 3,
            LabelMode = NullPresence.Null,
            LabelValue = string.Empty,
            NestedMode = NullPresence.Value,
            HostPresent = false,
            HostValue = "localhost",
            PortPresent = true,
            PortValue = 8080,
            PluginsPresent = true,
            PluginsText = "extra-plugin",
        };

    /// <summary>Builds a sparse fragment carrying only present members.</summary>
    public PlaygroundSettings.Fragment Build()
    {
        return new PlaygroundSettings.Fragment
        {
            Enabled = EnabledPresent
                ? Optional<bool>.Present(EnabledValue)
                : Optional<bool>.Missing,
            RetryCount = RetryPresent ? Optional<int>.Present(RetryValue) : Optional<int>.Missing,
            Label = LabelMode switch
            {
                NullPresence.Value => Optional<string?>.Present(LabelValue),
                NullPresence.Null => Optional<string?>.Present(null),
                _ => Optional<string?>.Missing,
            },
            Nested = NestedMode switch
            {
                NullPresence.Value => Optional<PlaygroundNested.Fragment?>.Present(BuildNested()),
                NullPresence.Null => Optional<PlaygroundNested.Fragment?>.Present(null),
                _ => Optional<PlaygroundNested.Fragment?>.Missing,
            },
            Plugins = PluginsPresent
                ? Optional<IReadOnlyList<string>>.Present(ParsePlugins(PluginsText))
                : Optional<IReadOnlyList<string>>.Missing,
        };
    }

    private PlaygroundNested.Fragment BuildNested()
    {
        return new PlaygroundNested.Fragment
        {
            Host = HostPresent ? Optional<string>.Present(HostValue) : Optional<string>.Missing,
            Port = PortPresent ? Optional<int>.Present(PortValue) : Optional<int>.Missing,
        };
    }

    /// <summary>Parses comma- or newline-separated plugin names.</summary>
    public static IReadOnlyList<string> ParsePlugins(string text) =>
        text.Split(
            [',', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
}

/// <summary>Editable operations for one <see cref="PlaygroundSettings.Patch"/>.</summary>
public sealed class PatchEditState
{
    /// <summary>Gets or sets the Enabled operation.</summary>
    public PatchKind EnabledOp { get; set; }

    /// <summary>Gets or sets the Enabled value for Set.</summary>
    public bool EnabledValue { get; set; }

    /// <summary>Gets or sets the RetryCount operation.</summary>
    public PatchKind RetryOp { get; set; }

    /// <summary>Gets or sets the RetryCount value for Set.</summary>
    public int RetryValue { get; set; } = 3;

    /// <summary>Gets or sets the Label operation.</summary>
    public NullPatchKind LabelOp { get; set; }

    /// <summary>Gets or sets the Label value for Set.</summary>
    public string LabelValue { get; set; } = string.Empty;

    /// <summary>Gets or sets the Nested operation.</summary>
    public NestedPatchKind NestedOp { get; set; }

    /// <summary>Gets or sets the Nested.Host operation.</summary>
    public PatchKind HostOp { get; set; }

    /// <summary>Gets or sets the Nested.Host value for Set.</summary>
    public string HostValue { get; set; } = string.Empty;

    /// <summary>Gets or sets the Nested.Port operation.</summary>
    public PatchKind PortOp { get; set; }

    /// <summary>Gets or sets the Nested.Port value for Set.</summary>
    public int PortValue { get; set; } = 9000;

    /// <summary>Gets or sets the Plugins operation.</summary>
    public PatchKind PluginsOp { get; set; }

    /// <summary>Gets or sets comma-separated plugin names for Set.</summary>
    public string PluginsText { get; set; } = string.Empty;

    /// <summary>Creates the default demo patch.</summary>
    public static PatchEditState Defaults() =>
        new()
        {
            EnabledOp = PatchKind.Unchanged,
            RetryOp = PatchKind.Unchanged,
            LabelOp = NullPatchKind.Set,
            LabelValue = "patched!",
            NestedOp = NestedPatchKind.Set,
            HostOp = PatchKind.Unchanged,
            PortOp = PatchKind.Set,
            PortValue = 9000,
            PluginsOp = PatchKind.Unchanged,
        };

    /// <summary>Builds the typed patch from the edited operations.</summary>
    public PlaygroundSettings.Patch Build()
    {
        var patch = new PlaygroundSettings.Patch();
        if (EnabledOp == PatchKind.Set)
        {
            patch.Enabled = FragmentOperation<bool>.Set(EnabledValue);
        }
        else if (EnabledOp == PatchKind.Unset)
        {
            patch.Enabled = FragmentOperation<bool>.Unset;
        }

        if (RetryOp == PatchKind.Set)
        {
            patch.RetryCount = FragmentOperation<int>.Set(RetryValue);
        }
        else if (RetryOp == PatchKind.Unset)
        {
            patch.RetryCount = FragmentOperation<int>.Unset;
        }

        if (LabelOp == NullPatchKind.Set)
        {
            patch.Label = FragmentOperation<string?>.Set(LabelValue);
        }
        else if (LabelOp == NullPatchKind.SetNull)
        {
            patch.Label = FragmentOperation<string?>.Set(null);
        }
        else if (LabelOp == NullPatchKind.Unset)
        {
            patch.Label = FragmentOperation<string?>.Unset;
        }

        if (NestedOp == NestedPatchKind.SetNull)
        {
            patch.Nested.SetNull();
        }
        else if (NestedOp == NestedPatchKind.Unset)
        {
            patch.Nested.Unset();
        }
        else if (NestedOp == NestedPatchKind.Set)
        {
            if (HostOp == PatchKind.Set)
            {
                patch.Nested.Host = FragmentOperation<string>.Set(HostValue);
            }
            else if (HostOp == PatchKind.Unset)
            {
                patch.Nested.Host = FragmentOperation<string>.Unset;
            }

            if (PortOp == PatchKind.Set)
            {
                patch.Nested.Port = FragmentOperation<int>.Set(PortValue);
            }
            else if (PortOp == PatchKind.Unset)
            {
                patch.Nested.Port = FragmentOperation<int>.Unset;
            }
        }

        if (PluginsOp == PatchKind.Set)
        {
            patch.Plugins = FragmentOperation<IReadOnlyList<string>>.Set(
                FragmentEditState.ParsePlugins(PluginsText)
            );
        }
        else if (PluginsOp == PatchKind.Unset)
        {
            patch.Plugins = FragmentOperation<IReadOnlyList<string>>.Unset;
        }

        return patch;
    }
}

/// <summary>One editable quest row for section 3.</summary>
public sealed class QuestRow
{
    /// <summary>Gets or sets the stable key.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the points.</summary>
    public int Points { get; set; }

    /// <summary>Gets or sets comma-separated scores.</summary>
    public string ScoresText { get; set; } = string.Empty;

    /// <summary>Creates a row from a model.</summary>
    public static QuestRow FromModel(PlaygroundQuest quest) =>
        new()
        {
            Id = quest.Id,
            Title = quest.Title,
            Points = quest.Points,
            ScoresText = string.Join(", ", quest.Scores),
        };

    /// <summary>Converts the row to a model.</summary>
    public PlaygroundQuest ToModel() =>
        new()
        {
            Id = Id,
            Title = Title,
            Points = Points,
            Scores = ParseScores(ScoresText),
        };

    /// <summary>Parses comma- or whitespace-separated integers.</summary>
    public static List<int> ParseScores(string text)
    {
        var result = new List<int>();
        foreach (
            var part in text.Split([',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
        )
        {
            if (int.TryParse(part.Trim(), out var value))
            {
                result.Add(value);
            }
        }

        return result;
    }
}

/// <summary>Editable before/after quest list for section 3.</summary>
public sealed class RosterEditState
{
    /// <summary>Gets the rows.</summary>
    public List<QuestRow> Rows { get; } = new();

    /// <summary>Creates the default before list.</summary>
    public static RosterEditState BeforeDefaults()
    {
        var state = new RosterEditState();
        state.Rows.Add(new QuestRow { Id = "a", Title = "First", Points = 10, ScoresText = "10, 20" });
        state.Rows.Add(new QuestRow { Id = "b", Title = "Second", Points = 20, ScoresText = "30" });
        state.Rows.Add(new QuestRow
        {
            Id = "c",
            Title = "Third",
            Points = 30,
            ScoresText = "40, 50",
        });
        return state;
    }

    /// <summary>Creates the default after list (remove a, edit b, reorder c first, add d).</summary>
    public static RosterEditState AfterDefaults()
    {
        var state = new RosterEditState();
        state.Rows.Add(new QuestRow
        {
            Id = "c",
            Title = "Third",
            Points = 30,
            ScoresText = "40, 50",
        });
        state.Rows.Add(new QuestRow
        {
            Id = "b",
            Title = "Second v2",
            Points = 25,
            ScoresText = "30, 35",
        });
        state.Rows.Add(new QuestRow { Id = "d", Title = "Fourth", Points = 5, ScoresText = "60" });
        return state;
    }

    /// <summary>Converts the rows to a model.</summary>
    public PlaygroundRoster ToModel() =>
        new() { Quests = Rows.Select(row => row.ToModel()).ToList() };

    /// <summary>Builds a fragment carrying the full list state.</summary>
    public PlaygroundRoster.Fragment BuildFragment() => PlaygroundRoster.Fragment.From(ToModel());
}

/// <summary>Per-row edit highlight for section 3, derived live from the keyed diff.</summary>
public sealed class RosterRowHighlight
{
    /// <summary>Gets or sets whether the key exists only in After.</summary>
    public bool IsAdded { get; set; }

    /// <summary>Gets or sets whether the key exists only in Before.</summary>
    public bool IsRemoved { get; set; }

    /// <summary>Gets or sets whether Title differs between Before and After.</summary>
    public bool TitleChanged { get; set; }

    /// <summary>Gets or sets whether Points differs between Before and After.</summary>
    public bool PointsChanged { get; set; }

    /// <summary>Gets or sets whether Scores differs between Before and After.</summary>
    public bool ScoresChanged { get; set; }

    /// <summary>Gets or sets whether the position differs between Before and After.</summary>
    public bool Moved { get; set; }
}

/// <summary>Builds before/after highlight maps from a generated roster ChangeSet.</summary>
/// <remarks>
/// Case 3 derives one <c>PlaygroundRoster.ChangeSet</c> from before/after state and
/// treats it as the source of truth. This is a thin mapping from the typed
/// <c>Quests</c> keyed transition (via <c>GetChange</c>) to the existing
/// CSS/highlight objects.
/// </remarks>
public static class RosterHighlight
{
    /// <summary>
    /// Projects an already-computed roster ChangeSet onto row highlights.
    /// Pure over the typed transition surface, so it stays testable without UI.
    /// </summary>
    public static (
        Dictionary<QuestRow, RosterRowHighlight> Before,
        Dictionary<QuestRow, RosterRowHighlight> After
    ) BuildFromChangeSet(
        PlaygroundRoster.ChangeSet changes,
        RosterEditState before,
        RosterEditState after
    )
    {
        var beforeMap = new Dictionary<QuestRow, RosterRowHighlight>();
        var afterMap = new Dictionary<QuestRow, RosterRowHighlight>();
        var quests = changes.Quests;
        if (quests.IsEmpty)
        {
            foreach (var row in before.Rows)
            {
                beforeMap[row] = new RosterRowHighlight();
            }

            foreach (var row in after.Rows)
            {
                afterMap[row] = new RosterRowHighlight();
            }

            return (beforeMap, afterMap);
        }

        foreach (var row in after.Rows)
        {
            var item = quests.GetChange(row.Id);
            var highlight = new RosterRowHighlight
            {
                IsAdded = item.IsAdded,
                Moved = item.IsReordered,
            };
            if (item.IsEdited)
            {
                highlight.TitleChanged = item.Edit.Title.IsChanged;
                highlight.PointsChanged = item.Edit.Points.IsChanged;
                highlight.ScoresChanged = item.Edit.Scores.IsChanged;
            }

            afterMap[row] = highlight;
        }

        foreach (var row in before.Rows)
        {
            var item = quests.GetChange(row.Id);
            beforeMap[row] = new RosterRowHighlight
            {
                IsRemoved = item.IsRemoved,
                Moved = item.IsReordered,
            };
        }

        return (beforeMap, afterMap);
    }
}

/// <summary>Trim-safe JSON helpers for the playground.</summary>
public static class PlaygroundJson
{
    /// <summary>Creates options carrying the generated fragment converters.</summary>
    public static JsonSerializerOptions FragmentOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = PlaygroundJsonContext.Default,
        };
        options.Converters.Add(new PlaygroundSettings.Fragment.FragmentJsonConverter());
        options.Converters.Add(new PlaygroundNested.Fragment.FragmentJsonConverter());
        options.Converters.Add(new PlaygroundRoster.Fragment.FragmentJsonConverter());
        options.Converters.Add(new PlaygroundQuest.Fragment.FragmentJsonConverter());
        return options;
    }

    /// <summary>Creates options carrying the source-generated metadata for ChangeSet JSON.</summary>
    public static JsonSerializerOptions ChangeSetOptions() =>
        new()
        {
            TypeInfoResolver = PlaygroundJsonContext.Default,
            WriteIndented = true,
        };

    /// <summary>Serializes a fragment to its canonical (present-members-only) JSON.</summary>
    public static string WriteFragment(PlaygroundSettings.Fragment fragment)
    {
        var options = FragmentOptions();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            new PlaygroundSettings.Fragment.FragmentJsonConverter().Write(
                writer,
                fragment,
                options
            );
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Serializes a roster fragment to its canonical (present-members-only) JSON.</summary>
    public static string WriteRosterFragment(PlaygroundRoster.Fragment fragment)
    {
        var options = FragmentOptions();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            new PlaygroundRoster.Fragment.FragmentJsonConverter().Write(
                writer,
                fragment,
                options
            );
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Serializes a roster model to indented JSON.</summary>
    public static string WriteRosterModel(PlaygroundRoster model)
    {
        var json = JsonSerializer.Serialize(
            model,
            PlaygroundJsonContext.Default.PlaygroundRoster
        );
        return Pretty(json);
    }

    /// <summary>Serializes a model to indented JSON.</summary>
    public static string WriteModel(PlaygroundSettings model)
    {
        var json = JsonSerializer.Serialize(
            model,
            PlaygroundJsonContext.Default.PlaygroundSettings
        );
        return Pretty(json);
    }

    /// <summary>Pretty-prints JSON, returning the input when it is not valid JSON.</summary>
    public static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(
                document,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
