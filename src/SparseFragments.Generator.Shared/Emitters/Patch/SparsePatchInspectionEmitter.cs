using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits <c>Patch.Changes</c> inspection projection (issue #73, generator side).</summary>
/// <remarks>
/// The runtime inspection vocabulary (<c>SparsePatchChange</c>,
/// <c>SparseKeyedCollectionInspection</c>, <c>SparseDictionaryInspection</c>, …)
/// ships with the runtime assembly; this emitter only projects patch state onto it.
/// Member fields are read directly (never through the lazy member getters) so
/// inspection never mutates the patch.
/// </remarks>
internal static class SparsePatchInspectionEmitter
{
    private const string Runtime = SparseFragmentPatchEmitter.Runtime;
    private const string Change = Runtime + "SparsePatchChange";
    private const string ChangeKind = Runtime + "SparseChangeKind";
    private const string OperationKind = Runtime + "FragmentOperationKind";
    private const string ChangeList = "global::System.Collections.Generic.List<" + Change + ">";
    private const string ReadOnlyChanges =
        "global::System.Collections.Generic.IReadOnlyList<" + Change + ">";
    private const string EmptyChanges = "global::System.Array.Empty<" + Change + ">()";

    /// <summary>Resolves the inspection property name, avoiding member collisions.</summary>
    internal static string ChangesPropertyName(ImmutableArray<SparseMemberModel> members)
    {
        if (members.IsDefaultOrEmpty)
        {
            return "Changes";
        }

        var names = new HashSet<string>(members.Select(static member => member.Property.Name));
        var prefix = new StringBuilder();
        while (names.Contains(prefix.ToString() + "Changes"))
        {
            prefix.Append("Sparse");
        }

        return prefix.ToString() + "Changes";
    }

    /// <summary>Emits the <c>Changes</c> property plus whole/get-changes hooks inside <c>Patch</c>.</summary>
    internal static void AppendPatchInspection(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members
    )
    {
        _ = modelType;
        var changesName = ChangesPropertyName(members);
        var escaped = SparseNaming.EscapeIdentifier(changesName);
        // Each member contributes at most one change. Small models never need
        // List<T>'s default capacity of four; allocate only on the first change.
        var compact = members.Length > 0 && members.Length < 4;
        var addChange = compact
            ? "(list ??= new(" + members.Length + ")).Add(new "
            : "list.Add(new ";
        code.AppendLineAt(
            2,
            "/// <summary>Gets the non-empty member changes in this patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Whole-patch Set/Unset applies to the entire model rather than one property, so it is excluded; only member changes are enumerated.</remarks>"
        );
        code.AppendLineAt(2, "public " + ReadOnlyChanges + " " + escaped);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            compact ? ChangeList + "? list = null;" : "var list = new " + ChangeList + "();"
        );
        foreach (var member in members)
        {
            code.CancellationToken.ThrowIfCancellationRequested();
            var field = SparseFragmentPatchEmitter.Field(member);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                AppendScalarChange(code, member, field, addChange);
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                AppendCollectionChange(code, member, field, addChange);
            }
            else
            {
                AppendChildChange(code, member, field, addChange);
            }
        }

        code.AppendLineAt(
            4,
            compact
                ? "return list is null ? " + EmptyChanges + " : (" + ReadOnlyChanges + ")list;"
                : "return list;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal "
                + Runtime
                + "FragmentOperation<Fragment?> __SparseWholeOperation => __sparse_whole;"
        );
        code.AppendLineAt(
            2,
            "internal " + ReadOnlyChanges + " __SparseGetChanges() => " + escaped + ";"
        );
    }

    private static void AppendScalarChange(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string addChange
    )
    {
        code.AppendLineAt(4, "if (" + field + ".Kind == " + OperationKind + ".Set)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + ChangeKind
                + ".Set, (object?)("
                + field
                + ".Value), "
                + EmptyChanges
                + ", null, null));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (" + field + ".Kind == " + OperationKind + ".Unset)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + ChangeKind
                + ".Unset, null, "
                + EmptyChanges
                + ", null, null));"
        );
        code.AppendLineAt(4, "}");
    }

    private static void AppendChildChange(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string addChange
    )
    {
        var child = "__sparse_child_" + member.Id;
        var whole = "__sparse_childWhole_" + member.Id;
        code.AppendLineAt(4, "var " + child + " = " + field + ";");
        code.AppendLineAt(4, "if (" + child + " is not null && !" + child + ".__SparseIsEmpty())");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var " + whole + " = " + child + ".__SparseWholeOperation;");
        code.AppendLineAt(5, "if (" + whole + ".Kind == " + OperationKind + ".Set)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + ChangeKind
                + ".Set, (object?)("
                + whole
                + ".Value), "
                + EmptyChanges
                + ", null, null));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (" + whole + ".Kind == " + OperationKind + ".Unset)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + ChangeKind
                + ".Unset, null, "
                + EmptyChanges
                + ", null, null));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + ChangeKind
                + ".Nested, null, "
                + child
                + ".__SparseGetChanges(), null, null));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }

    private static void AppendCollectionChange(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string addChange
    )
    {
        var collection = "__sparse_collection_" + member.Id;
        var whole = "__sparse_collectionWhole_" + member.Id;
        var granular = SparseKeyedCollectionEmitter.IsDictionary(member)
            ? "null, " + collection + ".__SparseInspectDictionary()"
            : collection + ".__SparseInspectKeyed(), null";
        var kind = SparseKeyedCollectionEmitter.IsDictionary(member)
            ? ChangeKind + ".Dictionary"
            : ChangeKind + ".KeyedCollection";
        code.AppendLineAt(4, "var " + collection + " = " + field + ";");
        code.AppendLineAt(
            4,
            "if (" + collection + " is not null && !" + collection + ".__SparseIsEmpty())"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var " + whole + " = " + collection + ".__SparseWholeOperation;");
        code.AppendLineAt(5, "if (" + whole + ".Kind == " + OperationKind + ".Set)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + ChangeKind
                + ".Set, (object?)("
                + whole
                + ".Value), "
                + EmptyChanges
                + ", null, null));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (" + whole + ".Kind == " + OperationKind + ".Unset)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + ChangeKind
                + ".Unset, null, "
                + EmptyChanges
                + ", null, null));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            addChange
                + Change
                + "(Sparse.Properties["
                + member.Id
                + "], "
                + kind
                + ", null, "
                + EmptyChanges
                + ", "
                + granular
                + "));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }

    /// <summary>Emits whole-accessor and keyed inspection inside a keyed-sequence patch.</summary>
    internal static void AppendKeyedInspection(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var listType = member.Property.Type.Name;
        code.AppendLineAt(
            3,
            "internal "
                + Runtime
                + "FragmentOperation<"
                + listType
                + "> __SparseWholeOperation => __whole;"
        );
        code.AppendLineAt(
            3,
            "internal " + Runtime + "SparseKeyedCollectionInspection __SparseInspectKeyed()"
        );
        code.AppendLineAt(3, "{");
        AppendObjectArrayCopy(code, "__sparse_added", "__added");
        AppendObjectArrayCopy(code, "__sparse_removed", "__removed");
        var editType = Runtime + "SparseKeyedEdit";
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.IReadOnlyList<"
                + editType
                + "> __sparse_edited = global::System.Array.Empty<"
                + editType
                + ">();"
        );
        code.AppendLineAt(4, "if (__edited is not null && __edited.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var __sparse_editedArray = new " + editType + "[__edited.Count];");
        code.AppendLineAt(5, "var __sparse_editedIndex = 0;");
        if (member.Collection.ElementType.IsFragmentModel)
        {
            code.AppendLineAt(
                5,
                "foreach (var __sparse_kvp in __edited) __sparse_editedArray[__sparse_editedIndex++] = new "
                    + editType
                    + "((object?)__sparse_kvp.Key, __sparse_kvp.Value.__SparseGetChanges());"
            );
        }
        else
        {
            // Unreachable by construction: keyed semantics apply only to
            // fragment/promotable elements, so __edited values always carry a
            // Patch. Projected without nested changes to stay compilable.
            code.AppendLineAt(
                5,
                "foreach (var __sparse_kvp in __edited) __sparse_editedArray[__sparse_editedIndex++] = new "
                    + editType
                    + "((object?)__sparse_kvp.Key, "
                    + EmptyChanges
                    + ");"
            );
        }

        code.AppendLineAt(5, "__sparse_edited = __sparse_editedArray;");
        code.AppendLineAt(4, "}");
        AppendObjectArrayCopy(code, "__sparse_order", "__order");
        code.AppendLineAt(
            4,
            "return new "
                + Runtime
                + "SparseKeyedCollectionInspection(__sparse_added, __sparse_removed, __sparse_edited, __order is not null, __sparse_order);"
        );
        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits whole-accessor and dictionary inspection inside a dictionary patch.</summary>
    internal static void AppendDictionaryInspection(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        var dictType = member.Property.Type.Name;
        code.AppendLineAt(
            3,
            "internal "
                + Runtime
                + "FragmentOperation<"
                + dictType
                + "> __SparseWholeOperation => __whole;"
        );
        code.AppendLineAt(
            3,
            "internal " + Runtime + "SparseDictionaryInspection __SparseInspectDictionary()"
        );
        code.AppendLineAt(3, "{");
        var entryType = Runtime + "SparseDictionaryEntry";
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.IReadOnlyList<"
                + entryType
                + "> __sparse_set = global::System.Array.Empty<"
                + entryType
                + ">();"
        );
        code.AppendLineAt(4, "if (__set is not null && __set.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var __sparse_setArray = new " + entryType + "[__set.Count];");
        code.AppendLineAt(5, "var __sparse_setIndex = 0;");
        code.AppendLineAt(
            5,
            "foreach (var __sparse_kvp in __set) __sparse_setArray[__sparse_setIndex++] = new "
                + entryType
                + "((object?)__sparse_kvp.Key, (object?)__sparse_kvp.Value);"
        );
        code.AppendLineAt(5, "__sparse_set = __sparse_setArray;");
        code.AppendLineAt(4, "}");
        AppendObjectArrayCopy(code, "__sparse_removed", "__removed");
        var editType = Runtime + "SparseDictionaryEdit";
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.IReadOnlyList<"
                + editType
                + "> __sparse_edited = global::System.Array.Empty<"
                + editType
                + ">();"
        );
        code.AppendLineAt(4, "if (__edited is not null && __edited.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var __sparse_editedArray = new " + editType + "[__edited.Count];");
        code.AppendLineAt(5, "var __sparse_editedIndex = 0;");
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            code.AppendLineAt(
                5,
                "foreach (var __sparse_kvp in __edited) __sparse_editedArray[__sparse_editedIndex++] = new "
                    + editType
                    + "((object?)__sparse_kvp.Key, null, __sparse_kvp.Value.__SparseGetChanges(), true);"
            );
        }
        else
        {
            code.AppendLineAt(
                5,
                "foreach (var __sparse_kvp in __edited) __sparse_editedArray[__sparse_editedIndex++] = new "
                    + editType
                    + "((object?)__sparse_kvp.Key, (object?)__sparse_kvp.Value, "
                    + EmptyChanges
                    + ", false);"
            );
        }

        code.AppendLineAt(5, "__sparse_edited = __sparse_editedArray;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return new "
                + Runtime
                + "SparseDictionaryInspection(__sparse_set, __sparse_removed, __sparse_edited);"
        );
        code.AppendLineAt(3, "}");
    }

    private static void AppendObjectArrayCopy(
        SharedIndentedBuilder code,
        string local,
        string field
    )
    {
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.IReadOnlyList<object?> "
                + local
                + " = global::System.Array.Empty<object?>();"
        );
        code.AppendLineAt(4, "if (" + field + " is not null && " + field + ".Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var " + local + "Array = new object?[" + field + ".Count];");
        code.AppendLineAt(5, "var " + local + "Index = 0;");
        code.AppendLineAt(
            5,
            "foreach (var __sparse_item in "
                + field
                + ") "
                + local
                + "Array["
                + local
                + "Index++] = (object?)__sparse_item;"
        );
        code.AppendLineAt(5, local + " = " + local + "Array;");
        code.AppendLineAt(4, "}");
    }
}
