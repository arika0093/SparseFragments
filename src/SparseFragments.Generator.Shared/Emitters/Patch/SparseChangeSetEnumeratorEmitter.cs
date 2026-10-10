using System.Collections.Generic;
using System.Collections.Immutable;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;

namespace SparseFragments.Generator.Shared;

internal static class SparseChangeSetEnumeratorEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null,
        string? modelType = null
    )
    {
        ComputePublicNames(members, out var propertyNames, out _);
        var runtime = dialect.RuntimeNamespace;
        var optionalObject = runtime + "Optional<object?>";
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        AppendChangeInfoTypes(code, optionalObject, pathType);
        if (target is not null)
        {
            var ops = target.ChangeSetOperations;
            AppendValueHelpers(ops, runtime, optionalObject, pathType, modelType);
            AppendMethodShellStub(code, target);
            AppendMethod(ops, members, propertyNames, optionalObject, pathType, modelType, target);
            AppendFindShellStub(code, dialect, modelType, target);
            AppendFind(ops, dialect, modelType, target);
        }
        else
        {
            AppendValueHelpers(code, runtime, optionalObject, pathType, modelType);
            AppendMethod(code, members, propertyNames, optionalObject, pathType, modelType, target);
            AppendFind(code, dialect, modelType, target);
        }
    }

    private static void AppendMethodShellStub(
        SharedIndentedBuilder code,
        SparseOperationTarget target
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Enumerates flattened value transitions, including keyed collection order changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerable<ChangeInfo> EnumerateChanges() => "
                + target.ChangeSetOperationsType
                + ".EnumerateChanges(this);"
        );
    }

    private static void AppendFindShellStub(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget target
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        code.AppendLineAt(
            2,
            "/// <summary>Finds the entry at exactly the given path, or null when absent.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public ChangeInfo? Find("
                + pathType
                + " path) => "
                + target.ChangeSetOperationsType
                + ".Find(this, path);"
        );
        if (modelType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Finds the entry at exactly the given typed path, or null when absent.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
            );
            code.AppendLineAt(
                2,
                "public ChangeInfo? Find<TValue>("
                    + SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue")
                    + " path) => "
                    + target.ChangeSetOperationsType
                    + ".Find(this, path);"
            );
        }
    }

    private static void AppendChangeInfoTypes(
        SharedIndentedBuilder code,
        string optionalObject,
        string pathType
    )
    {
        code.AppendLineAt(2, "/// <summary>Classifies a flattened change-set entry.</summary>");
        code.AppendLineAt(2, "public enum ChangeKind");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "/// <summary>A value changed while remaining present.</summary>");
        code.AppendLineAt(3, "Changed = 0,");
        code.AppendLineAt(3, "/// <summary>A value was added.</summary>");
        code.AppendLineAt(3, "Added = 1,");
        code.AppendLineAt(3, "/// <summary>A value was removed.</summary>");
        code.AppendLineAt(3, "Removed = 2,");
        code.AppendLineAt(3, "/// <summary>A keyed collection order changed.</summary>");
        code.AppendLineAt(3, "Order = 3,");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>A path and its before and after values in a change set.</summary>"
        );
        code.AppendLineAt(2, "public sealed class ChangeInfo");
        code.AppendLineAt(2, "{");
        // Public-first order: public properties precede the internal constructor.
        code.AppendLineAt(3, "/// <summary>Gets the changed member path.</summary>");
        code.AppendLineAt(3, "public " + pathType + " Path { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the wire-compatible path text.</summary>");
        code.AppendLineAt(3, "public string PathText => Path.ToString();");
        code.AppendLineAt(
            3,
            "/// <summary>Gets the presence-aware value before the change.</summary>"
        );
        code.AppendLineAt(3, "public " + optionalObject + " Before { get; }");
        code.AppendLineAt(
            3,
            "/// <summary>Gets the presence-aware value after the change.</summary>"
        );
        code.AppendLineAt(3, "public " + optionalObject + " After { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the kind of change.</summary>");
        code.AppendLineAt(3, "public ChangeKind Kind { get; }");
        code.AppendLineAt(
            3,
            "internal ChangeInfo("
                + pathType
                + " path, "
                + optionalObject
                + " before, "
                + optionalObject
                + " after, ChangeKind kind)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "Path = path;");
        code.AppendLineAt(4, "Before = before;");
        code.AppendLineAt(4, "After = after;");
        code.AppendLineAt(4, "Kind = kind;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendValueHelpers(
        SharedIndentedBuilder code,
        string runtime,
        string optionalObject,
        string pathType,
        string? modelType
    )
    {
        var optional = runtime + "Optional";
        code.AppendLineAt(
            2,
            "/// <summary>Model-rooted path factory for changes and rebase conflicts.</summary>"
        );
        code.AppendLineAt(
            2,
            "internal static "
                + pathType
                + " __SparseRootPath => "
                + pathType
                + ".Root(typeof("
                + (modelType ?? "global::System.Object")
                + "));"
        );
        code.AppendLineAt(
            2,
            "private static "
                + optionalObject
                + " __SparseBox<T>("
                + optional
                + "<T> value) => value.IsPresent ? "
                + optionalObject
                + ".Present(value.Value) : "
                + optionalObject
                + ".Missing;"
        );
        code.AppendLineAt(
            2,
            "private static ChangeInfo __SparseCreateChangeInfo<T>("
                + pathType
                + " path, "
                + optional
                + "<T> before, "
                + optional
                + "<T> after)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var kind = before.IsPresent == after.IsPresent ? ChangeKind.Changed : before.IsPresent ? ChangeKind.Removed : ChangeKind.Added;"
        );
        code.AppendLineAt(
            3,
            "return new ChangeInfo(path, __SparseBox(before), __SparseBox(after), kind);"
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string optionalObject,
        string pathType,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        var rootModel = modelType ?? "global::System.Object";
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates flattened value transitions, including keyed collection order changes.</summary>"
            );
            code.AppendLineAt(2, "/// <param name=\"self\">The change set to enumerate.</param>");
            code.AppendLineAt(2, "/// <returns>The flattened transitions.</returns>");
            code.AppendLineAt(
                2,
                "internal static global::System.Collections.Generic.IEnumerable<ChangeInfo> EnumerateChanges(ChangeSet self)"
            );
            code.AppendLineAt(2, "{");
            AppendSelfAliases(code, members);
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Enumerates flattened value transitions, including keyed collection order changes.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>A whole-root presence transition emits a single root entry that replaces member entries; nested whole-child presence transitions appear under the parent member path alongside other member entries. Presence-derived kinds apply: missing to present-null reads as <c>Added</c>, present-null to missing as <c>Removed</c>, and present-null to present-value as <c>Changed</c>. Each entry carries a canonical <c>SparsePath</c>: member segments, typed key segments for keyed and dictionary entries, index segments for positions, and set-membership segments carrying the element value. The wire text (<see cref=\"ChangeInfo.PathText\"/>) keeps the historical grammar: member segments joined by <c>.</c>; keyed, dictionary and set entries as <c>Name[\"key\"]</c> with the key JSON-escaped; order transitions at the member path with kind <c>Order</c>.</remarks>"
            );
            code.AppendLineAt(
                2,
                "public global::System.Collections.Generic.IEnumerable<ChangeInfo> EnumerateChanges()"
            );
            code.AppendLineAt(2, "{");
        }
        code.AppendLineAt(3, target is null ? "if (IsEmpty)" : "if (self.IsEmpty)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "return global::System.Array.Empty<ChangeInfo>();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var changes = new global::System.Collections.Generic.List<ChangeInfo>();"
        );
        // Root presence transitions carry no member values but are nonempty
        // history; report them instead of silently dropping them (issue #127).
        code.AppendLineAt(
            3,
            "var __sparse_prefix = " + pathType + ".Root(typeof(" + rootModel + "));"
        );
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "changes.Add(__SparseCreateChangeInfo(__sparse_prefix, __sparse_wholeBefore, __sparse_wholeAfter));"
        );
        code.AppendLineAt(4, "return changes;");
        code.AppendLineAt(3, "}");
        foreach (var member in members)
        {
            // Relocated bodies read transitions through the facade instance.
            var property =
                (target is null ? string.Empty : "self.")
                + SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
            var memberPath = "__sparse_member_" + member.Id;
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            code.AppendLineAt(
                3,
                "var " + memberPath + " = __sparse_prefix.Member(" + literal + ");"
            );
            if (IsNested(member))
            {
                AppendNestedChanges(code, property, memberPath, "nested" + member.Id, 3);
            }
            else if (IsKeyed(member))
            {
                AppendKeyedChanges(code, member, property, memberPath, optionalObject);
            }
            else if (IsDict(member))
            {
                AppendDictionaryChanges(code, member, property, memberPath);
            }
            else if (IsSet(member))
            {
                AppendSetChanges(code, member, property, memberPath, optionalObject);
            }
            else
            {
                AppendValueChange(code, property, memberPath, "value" + member.Id);
            }
        }

        code.AppendLineAt(3, "return changes;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendFind(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        var receiver = target is null ? string.Empty : "ChangeSet self, ";
        var enumerate = target is null ? "EnumerateChanges()" : "EnumerateChanges(self)";
        var visibility = target is null ? "public" : "internal static";
        code.AppendLineAt(
            2,
            "/// <summary>Finds the entry at exactly the given path, or null when absent.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
        );
        code.AppendLineAt(2, visibility + " ChangeInfo? Find(" + receiver + pathType + " path)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
        );
        code.AppendLineAt(3, "foreach (var change in " + enumerate + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (change.Path.Equals(path)) return change;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return null;");
        code.AppendLineAt(2, "}");
        if (modelType is null)
        {
            return;
        }

        code.AppendLineAt(
            2,
            "/// <summary>Finds the entry at exactly the given typed path, or null when absent.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Exact paths only; ancestors and descendants never match. The path root and value types are checked at compile time.</remarks>"
        );
        code.AppendLineAt(
            2,
            visibility
                + " ChangeInfo? Find<TValue>("
                + receiver
                + SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue")
                + " path)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
        );
        code.AppendLineAt(
            3,
            target is null ? "return Find(path.Path);" : "return Find(self, path.Path);"
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendValueChange(
        SharedIndentedBuilder code,
        string property,
        string memberPath,
        string local
    )
    {
        code.AppendLineAt(3, "var " + local + " = " + property + ";");
        code.AppendLineAt(3, "if (" + local + ".IsChanged)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + local
                + ".Before, "
                + local
                + ".After));"
        );
        code.AppendLineAt(3, "}");
    }

    private static void AppendSetChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath,
        string optionalObject
    )
    {
        // Set-element membership deltas at audit granularity (issue #174):
        // per-element entries carry the element value in a key segment and use
        // the comparer-aware typed deltas, so distinct values never share a path.
        var transition = "__sparse_set_transition_" + member.Id;
        var added = "__sparse_set_added_" + member.Id;
        var removed = "__sparse_set_removed_" + member.Id;
        code.AppendLineAt(3, "var " + transition + " = " + property + ";");
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
                + ".After.Value is null)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + transition
                + ".Before, "
                + transition
                + ".After));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "foreach (var " + added + " in " + transition + ".Added)");
        code.AppendLineAt(
            6,
            "changes.Add(new ChangeInfo("
                + memberPath
                + ".Key("
                + added
                + "), "
                + optionalObject
                + ".Missing, "
                + optionalObject
                + ".Present((object?)"
                + added
                + "), ChangeKind.Added));"
        );
        code.AppendLineAt(5, "foreach (var " + removed + " in " + transition + ".Removed)");
        code.AppendLineAt(
            6,
            "changes.Add(new ChangeInfo("
                + memberPath
                + ".Key("
                + removed
                + "), "
                + optionalObject
                + ".Present((object?)"
                + removed
                + "), "
                + optionalObject
                + ".Missing, ChangeKind.Removed));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void AppendNestedChanges(
        SharedIndentedBuilder code,
        string property,
        string memberPath,
        string suffix,
        int indent
    )
    {
        var change = "__sparse_nested_change_" + suffix;
        code.AppendLineAt(
            indent,
            "foreach (var " + change + " in " + property + ".EnumerateChanges())"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "changes.Add(new ChangeInfo("
                + memberPath
                + ".Append("
                + change
                + ".Path), "
                + change
                + ".Before, "
                + change
                + ".After, (ChangeKind)(int)"
                + change
                + ".Kind));"
        );
        code.AppendLineAt(indent, "}");
    }

    private static void AppendKeyedChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath,
        string optionalObject
    )
    {
        var transition = "__sparse_keyed_transition_" + member.Id;
        var item = "__sparse_keyed_item_" + member.Id;
        var itemPath = "__sparse_keyed_path_" + member.Id;
        var order = "__sparse_keyed_order_" + member.Id;
        code.AppendLineAt(3, "var " + transition + " = " + property + ";");
        code.AppendLineAt(
            3,
            "if (" + transition + ".Before.IsPresent || " + transition + ".After.IsPresent)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + transition
                + ".Before, "
                + transition
                + ".After));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var " + item + " in " + transition + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var " + itemPath + " = " + memberPath + ".Key(" + item + ".Key);");
        code.AppendLineAt(
            5,
            "if ("
                + item
                + ".IsAdded || "
                + item
                + ".IsRemoved) changes.Add(__SparseCreateChangeInfo("
                + itemPath
                + ", "
                + item
                + ".Before, "
                + item
                + ".After));"
        );
        code.AppendLineAt(5, "if (" + item + ".IsEdited)");
        code.AppendLineAt(5, "{");
        AppendNestedChanges(code, item + ".Edit", itemPath, "keyed" + member.Id, 5);
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (!global::System.Linq.Enumerable.SequenceEqual("
                + transition
                + ".BeforeOrder, "
                + transition
                + ".AfterOrder))"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var " + order + " = " + optionalObject + ".Present(" + transition + ".BeforeOrder);"
        );
        code.AppendLineAt(
            5,
            "var "
                + order
                + "After = "
                + optionalObject
                + ".Present("
                + transition
                + ".AfterOrder);"
        );
        code.AppendLineAt(
            5,
            "changes.Add(new ChangeInfo("
                + memberPath
                + ", "
                + order
                + ", "
                + order
                + "After, ChangeKind.Order));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void AppendDictionaryChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath
    )
    {
        var transition = "__sparse_dictionary_transition_" + member.Id;
        code.AppendLineAt(3, "if (!__sparse_hasWhole && !" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (" + KeyedItems(member) + " is not null)");
        code.AppendLineAt(4, "{");
        AppendDictionaryItems(code, member, KeyedItems(member), memberPath, 5);
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var " + transition + " = " + property + ";");
        code.AppendLineAt(
            4,
            "if (" + transition + ".Before.IsPresent || " + transition + ".After.IsPresent)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + transition
                + ".Before, "
                + transition
                + ".After));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        AppendDictionaryItems(code, member, transition, memberPath, 5);
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void AppendDictionaryItems(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string items,
        string memberPath,
        int indent
    )
    {
        var item = "__sparse_dictionary_item_" + member.Id;
        var itemPath = "__sparse_dictionary_path_" + member.Id;
        code.AppendLineAt(indent, "foreach (var " + item + " in " + items + ")");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var " + itemPath + " = " + memberPath + ".Key(" + item + ".Key);"
        );
        code.AppendLineAt(
            indent + 1,
            "if ("
                + item
                + ".IsAdded || "
                + item
                + ".IsRemoved) changes.Add(__SparseCreateChangeInfo("
                + itemPath
                + ", "
                + item
                + ".Before, "
                + item
                + ".After));"
        );
        code.AppendLineAt(indent + 1, "if (" + item + ".IsEdited)");
        code.AppendLineAt(indent + 1, "{");
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            AppendNestedChanges(
                code,
                item + ".Edit",
                itemPath,
                "dictionary" + member.Id,
                indent + 1
            );
        }
        else
        {
            code.AppendLineAt(
                indent + 2,
                "changes.Add(__SparseCreateChangeInfo("
                    + itemPath
                    + ", "
                    + item
                    + ".Before, "
                    + item
                    + ".After));"
            );
        }

        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }
}
