using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

internal static class SparseChangeSetPathEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseOperationTarget? target = null
    )
    {
        SparseChangeSetEmitter.ComputePublicNames(members, out var propertyNames, out _);
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>A whole-root presence transition reports <c>$root</c>, replacing member paths.</remarks>"
            );
            code.AppendLineAt(2, "/// <param name=\"prefix\">The path prefix.</param>");
            code.AppendLineAt(2, "/// <returns>The changed member paths.</returns>");
            code.AppendLineAt(
                2,
                "public global::System.Collections.Generic.IReadOnlyList<string> EnumerateChangedPaths(string prefix = \"\") => "
                    + target.ChangeSetOperationsType
                    + ".EnumerateChangedPaths(this, prefix);"
            );
            code = target.ChangeSetOperations;
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>A whole-root presence transition reports <c>$root</c>, replacing member paths.</remarks>"
            );
            code.AppendLineAt(2, "/// <param name=\"self\">The change set to enumerate.</param>");
            code.AppendLineAt(2, "/// <param name=\"prefix\">The path prefix.</param>");
            code.AppendLineAt(2, "/// <returns>The changed member paths.</returns>");
            code.AppendLineAt(
                2,
                "internal static global::System.Collections.Generic.IReadOnlyList<string> EnumerateChangedPaths(ChangeSet self, string prefix = \"\")"
            );
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates changed member paths, descending through nested models.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>A whole-root presence transition reports <c>$root</c>, replacing member paths.</remarks>"
            );
            code.AppendLineAt(2, "/// <param name=\"prefix\">The path prefix.</param>");
            code.AppendLineAt(2, "/// <returns>The changed member paths.</returns>");
            code.AppendLineAt(
                2,
                "public global::System.Collections.Generic.IReadOnlyList<string> EnumerateChangedPaths(string prefix = \"\")"
            );
        }
        code.AppendLineAt(2, "{");
        if (target is not null)
        {
            SparseChangeSetBasicsEmitter.AppendSelfAliases(code, members);
        }
        code.AppendLineAt(
            3,
            "if (prefix is null) throw new global::System.ArgumentNullException(nameof(prefix));"
        );
        code.AppendLineAt(3, "var paths = new global::System.Collections.Generic.List<string>();");
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "paths.Add(prefix.Length == 0 ? \"$root\" : prefix + \".\" + \"$root\");"
        );
        code.AppendLineAt(4, "return paths.AsReadOnly();");
        code.AppendLineAt(3, "}");
        foreach (var member in members)
        {
            var escapedProperty = SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
            // Relocated bodies read transitions through the facade instance.
            var transitionAccess = (target is null ? string.Empty : "self.") + escapedProperty;
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            var local = "__sparse_path_" + member.Id;
            code.AppendLineAt(
                3,
                "var "
                    + local
                    + " = prefix.Length == 0 ? "
                    + literal
                    + " : prefix + \".\" + "
                    + literal
                    + ";"
            );
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLineAt(
                    3,
                    "paths.AddRange(" + transitionAccess + ".EnumerateChangedPaths(" + local + "));"
                );
            }
            else if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                AppendKeyedPaths(code, member, transitionAccess, local);
            }
            else if (SparseChangeSetBasicsEmitter.IsDict(member))
            {
                AppendDictionaryPaths(code, member, transitionAccess, local);
            }
            else if (SparseChangeSetBasicsEmitter.IsSet(member))
            {
                AppendSetPaths(code, member, transitionAccess, local);
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "if (" + transitionAccess + ".IsChanged) paths.Add(" + local + ");"
                );
            }
        }

        code.AppendLineAt(3, "paths.Sort(global::System.StringComparer.Ordinal);");
        code.AppendLineAt(3, "return paths.AsReadOnly();");
        code.AppendLineAt(2, "}");
    }

    private static void AppendKeyedPaths(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath
    )
    {
        var item = "__sparse_item_" + member.Id;
        var index = "__sparse_index_" + member.Id;
        var itemPath = "__sparse_item_path_" + member.Id;
        code.AppendLineAt(3, "foreach (var " + item + " in " + property + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var "
                + index
                + " = "
                + item
                + ".AfterIndex >= 0 ? "
                + item
                + ".AfterIndex : "
                + item
                + ".BeforeIndex;"
        );
        code.AppendLineAt(
            4,
            "var "
                + itemPath
                + " = "
                + memberPath
                + " + \"[\" + "
                + index
                + ".ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\";"
        );
        code.AppendLineAt(
            4,
            "if ("
                + item
                + ".IsAdded || "
                + item
                + ".IsRemoved || "
                + item
                + ".IsReordered) paths.Add("
                + itemPath
                + ");"
        );
        code.AppendLineAt(4, "if (" + item + ".IsEdited)");
        code.AppendLineAt(4, "{");
        AppendNestedItemPaths(
            code,
            item + ".Edit",
            itemPath,
            item + ".IsAdded || " + item + ".IsRemoved"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void AppendDictionaryPaths(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath
    )
    {
        var item = "__sparse_item_" + member.Id;
        var itemPath = "__sparse_item_path_" + member.Id;
        var seen = "__sparse_seen_" + member.Id;
        code.AppendLineAt(
            3,
            "var " + seen + " = new global::System.Collections.Generic.HashSet<string>();"
        );
        code.AppendLineAt(3, "foreach (var " + item + " in " + property + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var "
                + itemPath
                + " = __SparseKeyPath("
                + seen
                + ", "
                + memberPath
                + ", "
                + item
                + ".Key);"
        );
        code.AppendLineAt(
            4,
            "if (" + item + ".IsAdded || " + item + ".IsRemoved) paths.Add(" + itemPath + ");"
        );
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            code.AppendLineAt(4, "if (" + item + ".IsEdited)");
            code.AppendLineAt(4, "{");
            AppendNestedItemPaths(
                code,
                item + ".Edit",
                itemPath,
                item + ".IsAdded || " + item + ".IsRemoved"
            );
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(
                4,
                "if ("
                    + item
                    + ".IsEdited && !("
                    + item
                    + ".IsAdded || "
                    + item
                    + ".IsRemoved)) paths.Add("
                    + itemPath
                    + ");"
            );
        }

        code.AppendLineAt(3, "}");
    }

    private static void AppendSetPaths(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath
    )
    {
        // Mirrors EnumerateChanges set flattening (issue #174): per-element
        // paths when both sides are present, one aggregate path otherwise.
        var transition = "__sparse_set_transition_" + member.Id;
        var seen = "__sparse_seen_" + member.Id;
        var added = "__sparse_set_added_" + member.Id;
        var removed = "__sparse_set_removed_" + member.Id;
        code.AppendLineAt(3, "var " + transition + " = " + property + ";");
        code.AppendLineAt(
            3,
            "var " + seen + " = new global::System.Collections.Generic.HashSet<string>();"
        );
        code.AppendLineAt(3, "if (" + transition + ".IsChanged)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!"
                + transition
                + ".Before.IsPresent || !"
                + transition
                + ".After.IsPresent || (object?)"
                + transition
                + ".Before.Value is null || (object?)"
                + transition
                + ".After.Value is null) paths.Add("
                + memberPath
                + ");"
        );
        code.AppendLineAt(
            4,
            "else { foreach (var "
                + added
                + " in "
                + transition
                + ".Added) paths.Add(__SparseKeyPath("
                + seen
                + ", "
                + memberPath
                + ", "
                + added
                + "));"
        );
        code.AppendLineAt(
            4,
            "foreach (var "
                + removed
                + " in "
                + transition
                + ".Removed) paths.Add(__SparseKeyPath("
                + seen
                + ", "
                + memberPath
                + ", "
                + removed
                + ")); }"
        );
        code.AppendLineAt(3, "}");
    }

    private static void AppendNestedItemPaths(
        SharedIndentedBuilder code,
        string changeSet,
        string itemPath,
        string isWholeItemChange
    )
    {
        var nestedPaths = "__sparse_nested_paths";
        code.AppendLineAt(
            5,
            "var " + nestedPaths + " = " + changeSet + ".EnumerateChangedPaths(" + itemPath + ");"
        );
        code.AppendLineAt(
            5,
            "if ("
                + nestedPaths
                + ".Count == 0 && !("
                + isWholeItemChange
                + ")) paths.Add("
                + itemPath
                + ");"
        );
        code.AppendLineAt(5, "paths.AddRange(" + nestedPaths + ");");
    }
}
