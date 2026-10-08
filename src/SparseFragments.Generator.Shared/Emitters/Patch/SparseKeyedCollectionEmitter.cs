using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits per-member keyed-sequence and dictionary patch types.</summary>
internal static class SparseKeyedCollectionEmitter
{
    public static bool IsCollectionPatch(SparseMemberModel member) =>
        member.Collection.IsKeyedSequence || member.Collection.IsDictionary;

    public static bool IsKeyedSequence(SparseMemberModel member) =>
        member.Collection.IsKeyedSequence;

    public static bool IsDictionary(SparseMemberModel member) => member.Collection.IsDictionary;

    public static string CollectionPatchName(SparseMemberModel member) =>
        SparseNaming.EscapeIdentifier(member.Property.Name) + "Patch";

    internal static string KeyType(SparseMemberModel member) =>
        IsDictionary(member)
            ? member.Collection.ElementType.Name
            : member.Collection.KeyTypeName ?? "object?";

    internal static string ElementType(SparseMemberModel member) =>
        member.Collection.ElementType.Name;

    internal static string MemberListType(SparseMemberModel member) => member.Property.Type.Name;

    internal static bool HasElementPatch(SparseMemberModel member) =>
        member.Collection.ElementType.IsFragmentModel;

    internal static bool HasValuePatch(SparseMemberModel member) =>
        member.Collection.ValueType?.IsFragmentModel == true;

    internal static void AppendPendingKeyLoopStart(
        SharedIndentedBuilder code,
        string baseMap = "baseMap",
        string currentMap = "currentMap",
        string desiredMap = "desiredMap"
    )
    {
        code.AppendLineAt(
            4,
            "for (var __mapIndex = 0; __mapIndex < 3 && __pendingKeys.Count > 0; __mapIndex++)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __keyMap = __mapIndex == 0 ? "
                + baseMap
                + " : (__mapIndex == 1 ? "
                + currentMap
                + " : "
                + desiredMap
                + ");"
        );
        code.IndentOffset++;
        code.AppendLineAt(4, "foreach (var k in __keyMap.Keys)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (__pendingKeys.Count == 0) { break; }");
        code.AppendLineAt(5, "if (!__pendingKeys.Remove(k)) { continue; }");
    }

    internal static void AppendPendingKeyLoopEnd(SharedIndentedBuilder code)
    {
        code.AppendLineAt(4, "}");
        code.IndentOffset--;
        code.AppendLineAt(4, "}");
    }

    internal static string ElementPatchType(SparseMemberModel member) =>
        member.Collection.ElementType.NonNullableName + ".Patch";

    internal static string ElementFragmentType(SparseMemberModel member) =>
        member.Collection.ElementType.NonNullableName + ".Fragment";

    internal static string ElementPatchPrefix(SparseMemberModel member) =>
        member.Collection.ElementType.PatchApiPrefix;

    internal static string ValuePatchType(SparseMemberModel member) =>
        member.Collection.ValueType!.Value.NonNullableName + ".Patch";

    internal static string ValueFragmentType(SparseMemberModel member) =>
        member.Collection.ValueType!.Value.NonNullableName + ".Fragment";

    internal static string ValuePatchPrefix(SparseMemberModel member) =>
        member.Collection.ValueType!.Value.PatchApiPrefix;

    internal static string DictionaryType(SparseMemberModel member) => member.Property.Type.Name;

    internal static bool IsInterfaceMember(SparseMemberModel member) =>
        member.Collection.NamedTypeDefinition is string definition
        && (
            definition.StartsWith("System.Collections.Generic.I", System.StringComparison.Ordinal)
            || definition.StartsWith("System.Collections.I", System.StringComparison.Ordinal)
        );

    internal static string DictionaryValueType(SparseMemberModel member) =>
        member.Collection.ValueType?.Name ?? "object?";

    public static void EmitCollectionPatches(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        foreach (var member in members)
        {
            if (!IsCollectionPatch(member))
            {
                continue;
            }

            if (IsDictionary(member))
            {
                SparseDictionaryPatchEmitter.EmitDictionaryPatch(code, member, dialect);
            }
            else
            {
                SparseKeyedSequenceSurfaceEmitter.EmitKeyedSequencePatch(code, member, dialect);
            }
        }
    }

    internal static string KeyOfMethod(SparseMemberModel member) => "__SparseKeyOf_" + member.Id;

    internal static void EmitKeyOf(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        int indent
    )
    {
        var keys = member.Collection.KeyPropertyNames;
        code.AppendLineAt(
            indent,
            "private static "
                + KeyType(member)
                + " "
                + KeyOfMethod(member)
                + "("
                + elementType
                + " element)"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
        );
        if (member.Collection.KeyKind == SparseKeyKind.Interface)
        {
            // ISparseKeyed<TKey>: computed/custom identity without reflection.
            code.AppendLineAt(indent + 1, "return element.SparseKey;");
        }
        else if (keys.Length == 1)
        {
            code.AppendLineAt(
                indent + 1,
                "return element." + SparseNaming.EscapeIdentifier(keys[0]) + ";"
            );
        }
        else
        {
            // Composite keys preserve type-level declaration order; the ValueTuple
            // representation is strongly typed and collision-safe by construction,
            // with component-wise EqualityComparer<T>.Default semantics.
            var tuple =
                "("
                + string.Join(
                    ", ",
                    keys.Select(static key => "element." + SparseNaming.EscapeIdentifier(key))
                )
                + ")";
            code.AppendLineAt(indent + 1, "return " + tuple + ";");
        }

        code.AppendLineAt(indent, "}");
    }

    internal static string MaterializeSequence(
        SparseMemberModel member,
        string variable,
        string runtime
    )
    {
        var collection = member.Collection;
        // CloneKind Array (includes IEnumerable/IReadOnlyList/array) -> ToArray; else new List.
        if (collection.CloneKind == SparseCloneCollectionKind.Array)
        {
            // Preserve T[]/IEnumerable shape: materialize then convert to member type via helper.
            // The member type may be T[] or IEnumerable<T>; ToArray covers both.
            // For IEnumerable<T> declared as List-compatible? Analyzer maps IEnumerable->Array kind.
            return runtime
                + "Optional<"
                + member.Property.Type.Name
                + ">.Present("
                + ConvertSequenceToMember(member, variable)
                + ")";
        }

        return runtime
            + "Optional<"
            + member.Property.Type.Name
            + ">.Present("
            + ConvertSequenceToMember(member, variable)
            + ")";
    }

    internal static string ConvertSequenceToMember(SparseMemberModel member, string variable)
    {
        // If member is List<T> or IList<T>, new List<T>(variable) when variable is already List<T> would copy; use variable directly if types match?
        // Simplest: if CloneKind is Array -> ToArray(variable); else if member type definition contains "List" -> new List<T>(variable) if needed else variable.
        if (member.Collection.CloneKind == SparseCloneCollectionKind.Array)
        {
            // variable is List<T>; member wants array/enumerable.
            // For T[] or IEnumerable<T>/IReadOnlyList<T>, ToArray then implicit? IEnumerable<T> accepts T[].
            // If member type is exactly T[], ToArray is perfect.
            return "global::System.Linq.Enumerable.ToArray(" + variable + ")";
        }

        // List shape: if variable is already List<T>, reuse when member is List<T>/IList<T>.
        return variable;
    }

    internal static string ConvertDictionaryToMember(SparseMemberModel member, string variable)
    {
        var definition = member.Collection.NamedTypeDefinition ?? string.Empty;
        var keyType = member.Collection.ElementType.Name;
        var valueType = member.Collection.ValueType?.Name ?? "object?";
        if (definition.Contains("SortedDictionary"))
        {
            return "new global::System.Collections.Generic.SortedDictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + variable
                + ")";
        }

        if (definition.Contains("SortedList"))
        {
            return "new global::System.Collections.Generic.SortedList<"
                + keyType
                + ", "
                + valueType
                + ">("
                + variable
                + ")";
        }

        return variable;
    }
}
