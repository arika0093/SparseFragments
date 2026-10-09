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
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        ComputePublicNames(members, out var propertyNames, out _);
        var runtime = dialect.RuntimeNamespace;
        var optionalObject = runtime + "Optional<object?>";
        AppendChangeInfoTypes(code, optionalObject);
        AppendValueHelpers(code, runtime, optionalObject);
        AppendMethod(code, members, propertyNames, optionalObject);
    }

    private static void AppendChangeInfoTypes(SharedIndentedBuilder code, string optionalObject)
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
        code.AppendLineAt(
            3,
            "internal ChangeInfo(string path, "
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
        code.AppendLineAt(3, "/// <summary>Gets the changed member path.</summary>");
        code.AppendLineAt(3, "public string Path { get; }");
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
        code.AppendLineAt(2, "}");
    }

    private static void AppendValueHelpers(
        SharedIndentedBuilder code,
        string runtime,
        string optionalObject
    )
    {
        var optional = runtime + "Optional";
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
            "private static ChangeInfo __SparseCreateChangeInfo<T>(string path, "
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
        code.AppendLineAt(2, "private static string __SparseKeyPath<T>(string path, T key)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var value = global::System.Convert.ToString(key, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;"
        );
        code.AppendLineAt(
            3,
            "return path + \"[\\\"\" + value.Replace(\"\\\\\", \"\\\\\\\\\").Replace(\"\\\"\", \"\\\\\\\"\") + \"\\\"]\";"
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string optionalObject
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Enumerates flattened value transitions, including keyed collection order changes.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>A whole-root presence transition emits a single <c>$root</c> entry that replaces member entries; nested whole-child presence transitions appear as <c>Parent.$root</c> alongside other member entries. Presence-derived kinds apply: missing to present-null reads as <c>Added</c>, present-null to missing as <c>Removed</c>, and present-null to present-value as <c>Changed</c>.</remarks>"
        );
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerable<ChangeInfo> EnumerateChanges()"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (IsEmpty)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "return global::System.Array.Empty<ChangeInfo>();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var changes = new global::System.Collections.Generic.List<ChangeInfo>();"
        );
        // Root presence transitions carry no member values but are nonempty
        // history; report them instead of silently dropping them (issue #127).
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "changes.Add(__SparseCreateChangeInfo(\"$root\", __sparse_wholeBefore, __sparse_wholeAfter));"
        );
        code.AppendLineAt(4, "return changes;");
        code.AppendLineAt(3, "}");
        foreach (var member in members)
        {
            var property = SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
            var path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            if (IsNested(member))
            {
                AppendNestedChanges(code, property, path, "nested" + member.Id, 3);
            }
            else if (IsKeyed(member))
            {
                AppendKeyedChanges(code, member, property, path, optionalObject);
            }
            else if (IsDict(member))
            {
                AppendDictionaryChanges(code, member, property, path);
            }
            else
            {
                AppendValueChange(code, property, path, "value" + member.Id);
            }
        }

        code.AppendLineAt(3, "return changes;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendValueChange(
        SharedIndentedBuilder code,
        string property,
        string path,
        string local
    )
    {
        code.AppendLineAt(3, "var " + local + " = " + property + ";");
        code.AppendLineAt(3, "if (" + local + ".IsChanged)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "changes.Add(__SparseCreateChangeInfo("
                + path
                + ", "
                + local
                + ".Before, "
                + local
                + ".After));"
        );
        code.AppendLineAt(3, "}");
    }

    private static void AppendNestedChanges(
        SharedIndentedBuilder code,
        string property,
        string path,
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
                + path
                + " + \".\" + "
                + change
                + ".Path, "
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
        string path,
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
                + path
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
        code.AppendLineAt(
            5,
            "var " + itemPath + " = __SparseKeyPath(" + path + ", " + item + ".Key);"
        );
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
                + path
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
        string path
    )
    {
        var transition = "__sparse_dictionary_transition_" + member.Id;
        code.AppendLineAt(3, "if (!__sparse_hasWhole && !" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (" + KeyedItems(member) + " is not null)");
        code.AppendLineAt(4, "{");
        AppendDictionaryItems(code, member, KeyedItems(member), path, 5);
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
                + path
                + ", "
                + transition
                + ".Before, "
                + transition
                + ".After));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        AppendDictionaryItems(code, member, transition, path, 5);
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void AppendDictionaryItems(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string items,
        string path,
        int indent
    )
    {
        var item = "__sparse_dictionary_item_" + member.Id;
        var itemPath = "__sparse_dictionary_path_" + member.Id;
        code.AppendLineAt(indent, "foreach (var " + item + " in " + items + ")");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var " + itemPath + " = __SparseKeyPath(" + path + ", " + item + ".Key);"
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
