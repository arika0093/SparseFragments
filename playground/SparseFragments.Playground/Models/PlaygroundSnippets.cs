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
            sb.AppendLine(
                $"{variableName}.Enabled = FragmentOperation<bool>.Set({BoolLiteral(state.EnabledValue)});"
            );
        }
        else if (state.EnabledOp == PatchKind.Unset)
        {
            sb.AppendLine(
                $"{variableName}.Enabled = FragmentOperation<bool>.Unset;"
            );
        }
        if (state.RetryOp == PatchKind.Set)
        {
            sb.AppendLine(
                $"{variableName}.RetryCount = FragmentOperation<int>.Set({state.RetryValue});"
            );
        }
        else if (state.RetryOp == PatchKind.Unset)
        {
            sb.AppendLine(
                $"{variableName}.RetryCount = FragmentOperation<int>.Unset;"
            );
        }
        if (state.LabelOp == NullPatchKind.Set)
        {
            sb.AppendLine(
                $"{variableName}.Label = FragmentOperation<string?>.Set({StringLiteral(state.LabelValue)});"
            );
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
                sb.AppendLine(
                    $"{variableName}.Nested.Host = FragmentOperation<string>.Set({StringLiteral(state.HostValue)});"
                );
            }
            else if (state.HostOp == PatchKind.Unset)
            {
                sb.AppendLine(
                    $"{variableName}.Nested.Host = FragmentOperation<string>.Unset;"
                );
            }
            if (state.PortOp == PatchKind.Set)
            {
                sb.AppendLine(
                    $"{variableName}.Nested.Port = FragmentOperation<int>.Set({state.PortValue});"
                );
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
            sb.AppendLine(
                $"{variableName}.Plugins = FragmentOperation<System.Collections.Generic.IReadOnlyList<string>>.Set({StringArrayLiteral(state.PluginsText)});"
            );
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
        sb.AppendLine("    Quests = new List<PlaygroundQuest>");
        sb.AppendLine("    {");
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
        sb.AppendLine("    },");
        sb.AppendLine("};");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the keyed Add/Remove/Edit/SetOrder patch for the inspected diff.
    /// Operations come from <c>patch.Changes</c> inspection with property
    /// identity resolved through <c>T.Sparse.Properties</c> (#72) rather than
    /// a second manual before/after comparison.
    /// </summary>
    public static string RosterManualPatchCSharp(
        string variableName,
        PlaygroundRoster.Patch patch,
        RosterEditState after
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine($"var {variableName} = new PlaygroundRoster.Patch();");
        var quests = RosterInspection.QuestsChange(patch);
        if (quests is null)
        {
            sb.AppendLine("// No quest changes.");
            return sb.ToString();
        }

        if (quests.Kind == SparseChangeKind.Set)
        {
            sb.AppendLine($"// Whole list replacement; granular Add/Remove/Edit do not apply.");
            sb.AppendLine(
                $"{variableName}.Quests.Set({QuestListLiteral(after.Rows.Select(static row => row.ToModel()).ToList())});"
            );
            return sb.ToString();
        }

        if (quests.Kind == SparseChangeKind.Unset || quests.Keyed is null)
        {
            sb.AppendLine($"{variableName}.Quests.Unset();");
            return sb.ToString();
        }

        var keyed = quests.Keyed;
        foreach (var added in keyed.Added)
        {
            var quest = (PlaygroundQuest)added!;
            sb.AppendLine($"{variableName}.Quests.Add(new PlaygroundQuest {{ Id = {StringLiteral(quest.Id)}, Title = {StringLiteral(quest.Title)}, Points = {quest.Points}, Scores = {IntListLiteral(quest.Scores)} }});");
        }
        foreach (var removed in keyed.RemovedKeys)
        {
            sb.AppendLine($"{variableName}.Quests.Remove({StringLiteral((string)removed!)});");
        }
        foreach (var edit in keyed.Edited)
        {
            var key = StringLiteral((string)edit.Key!);
            foreach (var change in edit.NestedChanges)
            {
                string editText;
                if (ReferenceEquals(change.Property, RosterInspection.QuestTitleProperty))
                {
                    editText = $"{variableName}.Quests.Edit({key}).Title = {StringLiteral((string)change.Value!)};";
                }
                else if (ReferenceEquals(change.Property, RosterInspection.QuestPointsProperty))
                {
                    editText = $"{variableName}.Quests.Edit({key}).Points = {change.Value};";
                }
                else if (ReferenceEquals(change.Property, RosterInspection.QuestScoresProperty))
                {
                    editText = $"{variableName}.Quests.Edit({key}).Scores = {IntListLiteral((List<int>)change.Value!)}; // whole value: one element change replaces the list";
                }
                else
                {
                    editText = $"// Unhandled quest member '{change.Property.Name}'.";
                }

                sb.AppendLine(editText);
            }
        }
        var order = keyed.HasOrder
            ? keyed.KeyOrder.Select(static key => (string)key!).ToList()
            : after.Rows.Select(static row => row.Id).ToList();
        sb.AppendLine($"{variableName}.Quests.SetOrder(new[] {{ {string.Join(", ", order.Select(StringLiteral))} }}); // final key order, not moves");
        return sb.ToString();
    }

    private static string IntListLiteral(IReadOnlyList<int> values) =>
        values.Count == 0 ? "new List<int>()" : $"new List<int> {{ {string.Join(", ", values)} }}";

    private static string QuestListLiteral(IReadOnlyList<PlaygroundQuest> quests)
    {
        if (quests.Count == 0)
        {
            return "new List<PlaygroundQuest>()";
        }

        var items = quests.Select(static quest =>
            $"new PlaygroundQuest {{ Id = {StringLiteral(quest.Id)}, Title = {StringLiteral(quest.Title)}, Points = {quest.Points}, Scores = {IntListLiteral(quest.Scores)} }}"
        );
        return $"new List<PlaygroundQuest> {{ {string.Join(", ", items)} }}";
    }
}
