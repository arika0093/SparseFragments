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
        SparseOperationTarget? target = null
    )
    {
        ComputePublicNames(members, out var propertyNames, out _);
        var runtime = dialect.RuntimeNamespace;
        var optionalObject = runtime + "Optional<object?>";
        AppendChangeInfoTypes(code, optionalObject);
        if (target is not null)
        {
            var ops = target.ChangeSetOperations;
            AppendValueHelpers(ops, runtime, optionalObject);
            AppendMethodShellStub(code, target);
            AppendMethod(ops, members, propertyNames, optionalObject, target);
        }
        else
        {
            AppendValueHelpers(code, runtime, optionalObject);
            AppendMethod(code, members, propertyNames, optionalObject, target);
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
        // Public-first order: public properties precede the internal constructor.
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
        // Stable, collision-resistant key text for supported key shapes
        // (issue #138). String representation alone is not injective: two
        // unequal composite keys may share a ToString(). Strings pass
        // through; formattable primitives keep invariant-culture text;
        // anything else is qualified by its runtime type name so distinct
        // types never share a path. Same-type display collisions are
        // disambiguated per enumeration by the deduplicating path helper.
        // The fallback uses only ToString/GetType: no JSON serialization,
        // so NativeAOT trimming stays clean.
        code.AppendLineAt(2, "private static string __SparseKeyText<T>(T key)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (key is null) return string.Empty;");
        code.AppendLineAt(3, "if (key is string text) return text;");
        code.AppendLineAt(
            3,
            "if (key is global::System.IFormattable formattable) return formattable.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;"
        );
        code.AppendLineAt(
            3,
            "return key.GetType().ToString() + \":\" + (global::System.Convert.ToString(key, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);"
        );
        code.AppendLineAt(2, "}");
        // Canonical bracket grammar (issue #155) composed with collision-resistant
        // key text (issue #138) and JSON escaping (issue #137): the bracket
        // wrapping flows through SparseCanonicalKeyPath.AppendEscaped so the
        // shared Name["key"] grammar cannot drift, while the text itself flows
        // through __SparseEscapeKey(__SparseKeyText(key)) plus per-enumeration
        // #2-style dedup. Raw Convert.ToString keys must not bypass this path.
        code.AppendLineAt(
            2,
            "private static string __SparseKeyPath<T>(global::System.Collections.Generic.HashSet<string> seen, string path, T key)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var text = __SparseEscapeKey(__SparseKeyText(key));");
        code.AppendLineAt(
            3,
            "var full = path" + SparseCanonicalKeyPath.AppendEscaped("text") + ";"
        );
        code.AppendLineAt(3, "if (seen.Add(full)) return full;");
        code.AppendLineAt(3, "var suffix = 2;");
        code.AppendLineAt(3, "while (true)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var candidate = path"
                + SparseCanonicalKeyPath.AppendEscaped(
                    "text + \"#\" + suffix.ToString(global::System.Globalization.CultureInfo.InvariantCulture)"
                )
                + ";"
        );
        code.AppendLineAt(4, "if (seen.Add(candidate)) return candidate;");
        code.AppendLineAt(4, "suffix++;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        // One canonical JSON-compatible escaping helper for keyed and
        // dictionary paths (issue #137). Only backslash and quote were
        // escaped before, emitting raw control characters that the Blazor
        // field parser (which reads quoted keys as JSON) cannot deserialize.
        code.AppendLineAt(
            2,
            "/// <summary>Escapes a key for use inside a quoted change path segment.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>JSON string escaping: backslash, quote, \\b \\f \\n \\r \\t, and remaining C0 controls as \\u00XX. Plain keys pass through unchanged.</remarks>"
        );
        code.AppendLineAt(2, "private static string __SparseEscapeKey(string value)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var needsEscape = false;");
        code.AppendLineAt(3, "foreach (var c in value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (c < 0x20 || c == '\"' || c == '\\\\')");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "needsEscape = true;");
        code.AppendLineAt(5, "break;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (!needsEscape) return value;");
        code.AppendLineAt(
            3,
            "var builder = new global::System.Text.StringBuilder(value.Length + 8);"
        );
        code.AppendLineAt(3, "foreach (var c in value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (c)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "case '\"': builder.Append(\"\\\\\\\"\"); break;");
        code.AppendLineAt(4, "case '\\\\': builder.Append(\"\\\\\\\\\"); break;");
        code.AppendLineAt(4, "case '\\b': builder.Append(\"\\\\b\"); break;");
        code.AppendLineAt(4, "case '\\f': builder.Append(\"\\\\f\"); break;");
        code.AppendLineAt(4, "case '\\n': builder.Append(\"\\\\n\"); break;");
        code.AppendLineAt(4, "case '\\r': builder.Append(\"\\\\r\"); break;");
        code.AppendLineAt(4, "case '\\t': builder.Append(\"\\\\t\"); break;");
        code.AppendLineAt(4, "default:");
        code.AppendLineAt(
            5,
            "if (c < 0x20) builder.Append(\"\\\\u\").Append(((int)c).ToString(\"x4\", global::System.Globalization.CultureInfo.InvariantCulture));"
        );
        code.AppendLineAt(5, "else builder.Append(c);");
        code.AppendLineAt(5, "break;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return builder.ToString();");
        code.AppendLineAt(2, "}");
    }

    private static void AppendMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string optionalObject,
        SparseOperationTarget? target = null
    )
    {
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
                "/// <remarks>A whole-root presence transition emits a single <c>$root</c> entry that replaces member entries; nested whole-child presence transitions appear as <c>Parent.$root</c> alongside other member entries. Presence-derived kinds apply: missing to present-null reads as <c>Added</c>, present-null to missing as <c>Removed</c>, and present-null to present-value as <c>Changed</c>. Path grammar: member segments joined by <c>.</c>; keyed and dictionary entries as <c>Name[\"key\"]</c> with the key JSON-escaped, so quoted segments always parse as JSON strings. Key text is collision-resistant: strings, invariant primitives and Guids keep their simple form, while other keys are qualified by their runtime type name; residual same-type display collisions are disambiguated per enumeration with a deterministic <c>#2</c>-style suffix. Set members emit per-element <c>Added</c>/<c>Removed</c> entries at <c>Name[\"element\"]</c> when both sides are present (comparer-aware deltas); whole set presence transitions emit one aggregate entry.</remarks>"
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
            // Relocated bodies read transitions through the facade instance.
            var property =
                (target is null ? string.Empty : "self.")
                + SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
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
            else if (IsSet(member))
            {
                AppendSetChanges(code, member, property, path, optionalObject);
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

    private static void AppendSetChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string path,
        string optionalObject
    )
    {
        // Set-element membership deltas at audit granularity (issue #174):
        // per-element entries reuse the collision-resistant key text and the
        // comparer-aware typed deltas, so distinct values never share a path.
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
                + ".After.Value is null)"
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
        code.AppendLineAt(5, "foreach (var " + added + " in " + transition + ".Added)");
        code.AppendLineAt(
            6,
            "changes.Add(new ChangeInfo(__SparseKeyPath("
                + seen
                + ", "
                + path
                + ", "
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
            "changes.Add(new ChangeInfo(__SparseKeyPath("
                + seen
                + ", "
                + path
                + ", "
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
        var seen = "__sparse_seen_" + member.Id;
        var item = "__sparse_keyed_item_" + member.Id;
        var itemPath = "__sparse_keyed_path_" + member.Id;
        var order = "__sparse_keyed_order_" + member.Id;
        code.AppendLineAt(3, "var " + transition + " = " + property + ";");
        code.AppendLineAt(
            3,
            "var " + seen + " = new global::System.Collections.Generic.HashSet<string>();"
        );
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
            "var " + itemPath + " = __SparseKeyPath(" + seen + ", " + path + ", " + item + ".Key);"
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
        var seen = "__sparse_seen_" + member.Id;
        code.AppendLineAt(
            indent,
            "var " + seen + " = new global::System.Collections.Generic.HashSet<string>();"
        );
        code.AppendLineAt(indent, "foreach (var " + item + " in " + items + ")");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var " + itemPath + " = __SparseKeyPath(" + seen + ", " + path + ", " + item + ".Key);"
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
