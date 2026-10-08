using System.Text;

namespace SparseFragments.Playground.Models;

/// <summary>C# snippet generator for the playground.</summary>
public static class PlaygroundSnippets
{
    /// <summary>Builds C# code constructing the given fragment layer.</summary>
    public static string FragmentCSharp(string variableName, FragmentEditState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"var {variableName} = new PlaygroundSettings.Fragment");
        sb.AppendLine("{");
        if (state.EnabledPresent)
        {
            sb.AppendLine($"    Enabled = {BoolLiteral(state.EnabledValue)},");
        }
        if (state.RetryPresent)
        {
            sb.AppendLine($"    RetryCount = {state.RetryValue},");
        }
        switch (state.LabelMode)
        {
            case NullPresence.Value:
                sb.AppendLine($"    Label = {StringLiteral(state.LabelValue)},");
                break;
            case NullPresence.Null:
                sb.AppendLine("    Label = (string?)null,");
                break;
        }
        switch (state.NestedMode)
        {
            case NullPresence.Value:
                sb.AppendLine($"    Nested = {NestedCSharp(state)},");
                break;
            case NullPresence.Null:
                sb.AppendLine("    Nested = (PlaygroundNested.Fragment?)null,");
                break;
        }
        if (state.PluginsPresent)
        {
            sb.AppendLine($"    Plugins = {StringArrayLiteral(state.PluginsText)},");
        }
        sb.AppendLine("};");
        return sb.ToString();
    }

    /// <summary>Builds C# code constructing a model value.</summary>
    public static string ModelCSharp(string variableName, PlaygroundSettings model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"var {variableName} = new PlaygroundSettings");
        sb.AppendLine("{");
        sb.AppendLine($"    Enabled = {BoolLiteral(model.Enabled)},");
        sb.AppendLine($"    RetryCount = {model.RetryCount},");
        sb.AppendLine(
            model.Label is null ? "    Label = null," : $"    Label = {StringLiteral(model.Label)},"
        );
        if (model.Nested is null)
        {
            sb.AppendLine("    Nested = null,");
        }
        else
        {
            sb.AppendLine("    Nested = new PlaygroundNested");
            sb.AppendLine("    {");
            sb.AppendLine($"        Host = {StringLiteral(model.Nested.Host)},");
            sb.AppendLine($"        Port = {model.Nested.Port},");
            sb.AppendLine("    },");
        }
        sb.AppendLine($"    Plugins = {StringArrayLiteral(model.Plugins)},");
        sb.AppendLine("};");
        return sb.ToString();
    }

    /// <summary>Builds C# code constructing the edited typed patch.</summary>
    public static string PatchCSharp(string variableName, PatchEditState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"var {variableName} = new PlaygroundSettings.Patch();");
        AppendPatchLine(sb, variableName, state);
        return sb.ToString();
    }

    private static void AppendPatchLine(StringBuilder sb, string variableName, PatchEditState state)
    {
        if (state.EnabledOp == PatchKind.Set)
        {
            sb.AppendLine($"{variableName}.Enabled = {BoolLiteral(state.EnabledValue)};");
        }
        else if (state.EnabledOp == PatchKind.Unset)
        {
            sb.AppendLine(
                $"{variableName}.Enabled = FragmentOperation<bool>.Unset;"
            );
        }
        if (state.RetryOp == PatchKind.Set)
        {
            sb.AppendLine($"{variableName}.RetryCount = {state.RetryValue};");
        }
        else if (state.RetryOp == PatchKind.Unset)
        {
            sb.AppendLine(
                $"{variableName}.RetryCount = FragmentOperation<int>.Unset;"
            );
        }
        if (state.LabelOp == NullPatchKind.Set)
        {
            sb.AppendLine($"{variableName}.Label = {StringLiteral(state.LabelValue)};");
        }
        else if (state.LabelOp == NullPatchKind.SetNull)
        {
            sb.AppendLine(
                $"{variableName}.Label = FragmentOperation<string?>.Set(null);"
            );
        }
        else if (state.LabelOp == NullPatchKind.Unset)
        {
            sb.AppendLine(
                $"{variableName}.Label = FragmentOperation<string?>.Unset;"
            );
        }
        if (state.NestedOp == NestedPatchKind.SetNull)
        {
            sb.AppendLine($"{variableName}.Nested.SetNull();");
        }
        else if (state.NestedOp == NestedPatchKind.Unset)
        {
            sb.AppendLine($"{variableName}.Nested.Unset();");
        }
        else if (state.NestedOp == NestedPatchKind.Set)
        {
            if (state.HostOp == PatchKind.Set)
            {
                sb.AppendLine($"{variableName}.Nested.Host = {StringLiteral(state.HostValue)};");
            }
            else if (state.HostOp == PatchKind.Unset)
            {
                sb.AppendLine(
                    $"{variableName}.Nested.Host = FragmentOperation<string>.Unset;"
                );
            }
            if (state.PortOp == PatchKind.Set)
            {
                sb.AppendLine($"{variableName}.Nested.Port = {state.PortValue};");
            }
            else if (state.PortOp == PatchKind.Unset)
            {
                sb.AppendLine(
                    $"{variableName}.Nested.Port = FragmentOperation<int>.Unset;"
                );
            }
        }
        if (state.PluginsOp == PatchKind.Set)
        {
            sb.AppendLine($"{variableName}.Plugins = {StringArrayLiteral(state.PluginsText)};");
        }
        else if (state.PluginsOp == PatchKind.Unset)
        {
            sb.AppendLine(
                $"{variableName}.Plugins = FragmentOperation<System.Collections.Generic.IReadOnlyList<string>>.Unset;"
            );
        }
    }

    private static string NestedCSharp(FragmentEditState state)
    {
        var parts = new List<string>();
        if (state.HostPresent)
        {
            parts.Add($"Host = {StringLiteral(state.HostValue)}");
        }
        if (state.PortPresent)
        {
            parts.Add($"Port = {state.PortValue}");
        }
        if (parts.Count == 0)
        {
            return "new PlaygroundNested.Fragment()";
        }
        var nested = new StringBuilder();
        nested.AppendLine("new PlaygroundNested.Fragment");
        nested.AppendLine("    {");
        foreach (var part in parts)
        {
            nested.AppendLine($"        {part},");
        }
        nested.Append("    }");
        return nested.ToString();
    }

    private static string BoolLiteral(bool value) => value ? "true" : "false";

    private static string StringLiteral(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string StringArrayLiteral(string text) =>
        StringArrayLiteral(FragmentEditState.ParsePlugins(text));

    private static string StringArrayLiteral(IReadOnlyList<string> values) =>
        values.Count == 0 ? "[]" : $"[{string.Join(", ", values.Select(StringLiteral))}]";

    /// <summary>Builds C# code constructing a roster model value.</summary>
    public static string RosterModelCSharp(string variableName, PlaygroundRoster model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"var {variableName} = new PlaygroundRoster");
        sb.AppendLine("{");
        sb.AppendLine("    Quests =");
        sb.AppendLine("    [");
        foreach (var quest in model.Quests)
        {
            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            sb.AppendLine($"            Id = {StringLiteral(quest.Id)},");
            sb.AppendLine($"            Title = {StringLiteral(quest.Title)},");
            sb.AppendLine($"            Points = {quest.Points},");
            sb.AppendLine($"            Scores = {IntListLiteral(quest.Scores)},");
            sb.AppendLine("        },");
        }
        sb.AppendLine("    ],");
        sb.AppendLine("};");
        return sb.ToString();
    }

    /// <summary>
    /// One-line diff summary projected from an already-computed roster ChangeSet.
    /// Presentation formatting only; semantic detection stays in the typed transition.
    /// </summary>
    public static string RosterSummary(PlaygroundRoster.ChangeSet changes)
    {
        var quests = changes.Quests;
        if (quests.IsEmpty)
        {
            return "no changes";
        }

        var added = quests.Added.Select(static quest => quest.Id).ToList();
        var removed = quests.Removed.Select(static quest => quest.Id).ToList();
        var edited = quests.Edited.Select(static edit => edit.Key).ToList();
        var order = quests.AfterOrder.ToList();
        return $"add: [{string.Join(", ", added)}] | remove: [{string.Join(", ", removed)}] | edit: [{string.Join(", ", edited)}] | order: [{string.Join("→", order)}]";
    }

    /// <summary>Builds the model extension call deriving the roster ChangeSet.</summary>
    public static string RosterChangeSetCSharp(string baselineName, string currentName) =>
        $"var changes = {baselineName}.CreateChangeSet({currentName});";

    /// <summary>
    /// Builds the keyed Add/Remove/Edit/SetOrder patch for the typed diff.
    /// Operations come from the <c>Quests</c> keyed transition on
    /// <c>PlaygroundRoster.ChangeSet</c> rather than a second manual
    /// before/after comparison.
    /// </summary>
    public static string RosterManualPatchCSharp(
        string variableName,
        PlaygroundRoster.ChangeSet changes
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine($"var {variableName} = new PlaygroundRoster.Patch();");
        var quests = changes.Quests;
        if (quests.IsEmpty)
        {
            sb.AppendLine("// No quest changes.");
            return sb.ToString();
        }

        foreach (var added in quests.Added)
        {
            sb.AppendLine($"{variableName}.Quests.Add(new PlaygroundQuest {{ Id = {StringLiteral(added.Id)}, Title = {StringLiteral(added.Title)}, Points = {added.Points}, Scores = {IntListLiteral(added.Scores)} }});");
        }
        foreach (var removed in quests.Removed)
        {
            sb.AppendLine($"{variableName}.Quests.Remove({StringLiteral(removed.Id)});");
        }
        foreach (var edit in quests.Edited)
        {
            var key = StringLiteral(edit.Key);
            var quest = edit.Value;
            if (quest.Title.IsChanged && quest.Title.After.IsPresent)
            {
                sb.AppendLine($"{variableName}.Quests.Edit({key}).Title = {StringLiteral(quest.Title.After.Value ?? string.Empty)};");
            }

            if (quest.Points.IsChanged && quest.Points.After.IsPresent)
            {
                sb.AppendLine($"{variableName}.Quests.Edit({key}).Points = {quest.Points.After.Value};");
            }

            if (quest.Scores.IsChanged && quest.Scores.After.IsPresent)
            {
                sb.AppendLine($"{variableName}.Quests.Edit({key}).Scores = {IntListLiteral(quest.Scores.After.Value ?? new List<int>())}; // whole value: one element change replaces the list");
            }
        }
        sb.AppendLine($"{variableName}.Quests.SetOrder([{string.Join(", ", quests.AfterOrder.Select(StringLiteral))}]); // final key order, not moves");
        return sb.ToString();
    }

    private static string IntListLiteral(IReadOnlyList<int> values) =>
        $"[{string.Join(", ", values)}]";
}
