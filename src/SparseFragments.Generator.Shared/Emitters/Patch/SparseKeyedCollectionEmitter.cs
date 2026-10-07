using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

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

    private static string KeyType(SparseMemberModel member) =>
        IsDictionary(member)
            ? member.Collection.ElementType.Name
            : member.Collection.KeyTypeName ?? "object?";

    private static string ElementType(SparseMemberModel member) =>
        member.Collection.ElementType.Name;

    private static string MemberListType(SparseMemberModel member) => member.Property.Type.Name;

    private static bool HasElementPatch(SparseMemberModel member) =>
        member.Collection.ElementType.IsFragmentModel;

    private static bool HasValuePatch(SparseMemberModel member) =>
        member.Collection.ValueType?.IsFragmentModel == true;

    private static void AppendPendingKeyLoopStart(
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

    private static void AppendPendingKeyLoopEnd(SharedIndentedBuilder code)
    {
        code.AppendLineAt(4, "}");
        code.IndentOffset--;
        code.AppendLineAt(4, "}");
    }

    private static string ElementPatchType(SparseMemberModel member) =>
        member.Collection.ElementType.NonNullableName + ".Patch";

    private static string ElementFragmentType(SparseMemberModel member) =>
        member.Collection.ElementType.NonNullableName + ".Fragment";

    private static string ElementPatchPrefix(SparseMemberModel member) =>
        member.Collection.ElementType.PatchApiPrefix;

    private static string ValuePatchType(SparseMemberModel member) =>
        member.Collection.ValueType!.Value.NonNullableName + ".Patch";

    private static string ValueFragmentType(SparseMemberModel member) =>
        member.Collection.ValueType!.Value.NonNullableName + ".Fragment";

    private static string ValuePatchPrefix(SparseMemberModel member) =>
        member.Collection.ValueType!.Value.PatchApiPrefix;

    private static string DictionaryType(SparseMemberModel member) => member.Property.Type.Name;

    private static bool IsInterfaceMember(SparseMemberModel member) =>
        member.Collection.NamedTypeDefinition is string definition
        && (
            definition.StartsWith("System.Collections.Generic.I", System.StringComparison.Ordinal)
            || definition.StartsWith("System.Collections.I", System.StringComparison.Ordinal)
        );

    private static string DictionaryValueType(SparseMemberModel member) =>
        member.Collection.ValueType?.Name ?? "object?";

    public static void EmitCollectionPatches(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
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
                EmitDictionaryPatch(code, member);
            }
            else
            {
                EmitKeyedSequencePatch(code, member);
            }
        }
    }

    private static string KeyOfMethod(SparseMemberModel member) => "__SparseKeyOf_" + member.Id;

    private static void EmitKeyOf(
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

    private static void EmitKeyedSequencePatch(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var patchName = CollectionPatchName(member);
        var elementType = ElementType(member);
        var keyType = KeyType(member);
        var listType = MemberListType(member);
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var operation = runtime + "FragmentOperation<" + listType + ">";
        var kind = runtime + "FragmentOperationKind";
        var optionalList = runtime + "Optional<" + listType + ">";
        var facade = "global::SparseFragments.CompilerServices.SparseFragmentRuntime";
        var hasPatch = HasElementPatch(member);
        var editedValueType = hasPatch ? ElementPatchType(member) : elementType;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";

        code.AppendLineAt(
            2,
            "/// <summary>Keyed collection patch for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "public sealed class " + patchName);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "private " + operation + " __whole;");
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.List<" + elementType + ">? __added;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.List<" + keyType + ">? __removed;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedValueType
                + ">? __edited;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.List<" + keyType + ">? __order;"
        );
        EmitKeyOf(code, member, elementType, 3);
        code.AppendLineAt(3, "public bool IsEmpty => __whole.Kind == " + kind + ".Unchanged");
        code.AppendLineAt(
            4,
            "&& (__added is null || __added.Count == 0) && (__removed is null || __removed.Count == 0)"
        );
        code.AppendLineAt(
            4,
            "&& (__edited is null || __edited.Count == 0) && (__order is null || __order.Count == 0);"
        );
        code.AppendLineAt(3, "internal bool __SparseIsEmpty() => IsEmpty;");
        // Whole operations.
        code.AppendLineAt(3, "public void Set(" + listType + " value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__whole = " + operation + ".Set(value);");
        code.AppendLineAt(4, "__added = null; __removed = null; __edited = null; __order = null;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public void Unset()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__whole = " + operation + ".Unset;");
        code.AppendLineAt(4, "__added = null; __removed = null; __edited = null; __order = null;");
        code.AppendLineAt(3, "}");
        if (!IsInterfaceMember(member))
        {
            code.AppendLineAt(
                3,
                "public static implicit operator "
                    + patchName
                    + "("
                    + listType
                    + " value) => new "
                    + patchName
                    + " { __whole = "
                    + operation
                    + ".Set(value) };"
            );
        }
        code.AppendLineAt(3, "private void EnsureGranular(string operation)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__whole.Kind != "
                + kind
                + ".Unchanged) throw new global::System.InvalidOperationException(\"Cannot apply '\" + operation + \"' when the whole collection is set. Clear the whole operation first.\");"
        );
        code.AppendLineAt(3, "}");
        // Add.
        code.AppendLineAt(3, "public void Add(" + elementType + " element)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));"
        );
        code.AppendLineAt(4, "EnsureGranular(\"Add\");");
        code.AppendLineAt(4, "var key = " + KeyOfMethod(member) + "(element);");
        code.AppendLineAt(
            4,
            "if (__added is not null) foreach (var existing in __added) if ("
                + comparer
                + ".Equals("
                + KeyOfMethod(member)
                + "(existing), key)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection patch.\");"
        );
        code.AppendLineAt(
            4,
            "if (__edited is not null && __edited.ContainsKey(key)) throw new global::System.InvalidOperationException(\"Key is already edited in this patch.\");"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null) __removed.RemoveAll(k => " + comparer + ".Equals(k, key));"
        );
        code.AppendLineAt(
            4,
            "__added ??= new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(4, "__added.Add(element);");
        code.AppendLineAt(3, "}");
        // Remove.
        code.AppendLineAt(3, "public void Remove(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "EnsureGranular(\"Remove\");");
        code.AppendLineAt(
            4,
            "if (__added is not null) { for (var i = __added.Count - 1; i >= 0; i--) if ("
                + comparer
                + ".Equals("
                + KeyOfMethod(member)
                + "(__added[i]), key)) { __added.RemoveAt(i); return; } }"
        );
        code.AppendLineAt(4, "if (__edited is not null) __edited.Remove(key);");
        code.AppendLineAt(
            4,
            "__removed ??= new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            4,
            "foreach (var existing in __removed) if ("
                + comparer
                + ".Equals(existing, key)) return;"
        );
        code.AppendLineAt(4, "__removed.Add(key);");
        code.AppendLineAt(3, "}");
        // Edit / Update.
        if (hasPatch)
        {
            var elementPatch = ElementPatchType(member);
            code.AppendLineAt(3, "public " + elementPatch + " Edit(" + keyType + " key)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "EnsureGranular(\"Edit\");");
            code.AppendLineAt(
                4,
                "if (__removed is not null) foreach (var r in __removed) if ("
                    + comparer
                    + ".Equals(r, key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed element. Add it again instead.\");"
            );
            code.AppendLineAt(
                4,
                "if (__added is not null) foreach (var a in __added) if ("
                    + comparer
                    + ".Equals("
                    + KeyOfMethod(member)
                    + "(a), key)) throw new global::System.InvalidOperationException(\"Cannot edit an element added in the same patch. Update the added value directly.\");"
            );
            code.AppendLineAt(
                4,
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + editedValueType
                    + "> ("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (!__edited.TryGetValue(key, out var patch)) { patch = new "
                    + elementPatch
                    + "(); __edited[key] = patch; }"
            );
            code.AppendLineAt(4, "return patch;");
            code.AppendLineAt(3, "}");
        }
        else
        {
            code.AppendLineAt(3, "public void Update(" + elementType + " element)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));"
            );
            code.AppendLineAt(4, "EnsureGranular(\"Update\");");
            code.AppendLineAt(4, "var key = " + KeyOfMethod(member) + "(element);");
            code.AppendLineAt(
                4,
                "if (__removed is not null) foreach (var r in __removed) if ("
                    + comparer
                    + ".Equals(r, key)) throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
            );
            code.AppendLineAt(
                4,
                "if (__added is not null) for (var i = 0; i < __added.Count; i++) if ("
                    + comparer
                    + ".Equals("
                    + KeyOfMethod(member)
                    + "(__added[i]), key)) { __added[i] = element; return; }"
            );
            code.AppendLineAt(
                4,
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + editedValueType
                    + "> ("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(4, "__edited[key] = element;");
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(
            3,
            "public void SetOrder(global::System.Collections.Generic.IEnumerable<"
                + keyType
                + "> keys)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (keys is null) throw new global::System.ArgumentNullException(nameof(keys));"
        );
        code.AppendLineAt(4, "EnsureGranular(\"SetOrder\");");
        code.AppendLineAt(
            4,
            "var list = new global::System.Collections.Generic.List<" + keyType + ">(keys);"
        );
        code.AppendLineAt(4, facade + ".EnsureUniqueKeys<" + keyType + ">(list);");
        code.AppendLineAt(4, "__order = list;");
        code.AppendLineAt(3, "}");
        // Issue #103: internal sparse setter for ChangeSet.ToPatch (nested fragment edits).
        // Bypasses the public Edit-for-mutation API by installing an already-built
        // element patch; used only to project canonical ChangeSet transitions.
        if (hasPatch)
        {
            code.AppendLineAt(
                3,
                "internal void __SparseSetEdited("
                    + keyType
                    + " key, "
                    + ElementPatchType(member)
                    + " patch)"
            );
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "EnsureGranular(\"SetEdited\");");
            code.AppendLineAt(
                4,
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            code.AppendLineAt(
                4,
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + ElementPatchType(member)
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(4, "__edited[key] = patch;");
            code.AppendLineAt(3, "}");
        }
        EmitKeyedApply(code, member, elementType, keyType, listType, hasPatch, comparer);
        EmitKeyedBetween(
            code,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            facade
        );
        EmitKeyedCompose(
            code,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer
        );
        code.AppendLineAt(
            3,
            "public "
                + patchName
                + " Invert("
                + optionalList
                + " baseline) => Between(Apply(baseline), baseline);"
        );
        EmitKeyedRebase(
            code,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            facade
        );
        SparsePatchStjEmitter.AppendKeyedStj(code, member);
        code.AppendLineAt(2, "}");
    }

    private static void EmitKeyedApply(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer
    )
    {
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var optionalList = runtime + "Optional<" + listType + ">";
        var kind = runtime + "FragmentOperationKind";
        code.AppendLineAt(3, "public " + optionalList + " Apply(" + optionalList + " current)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__whole.Kind != " + kind + ".Unchanged) return __whole.Apply(current);"
        );
        code.AppendLineAt(4, "if (IsEmpty) return current;");
        code.AppendLineAt(
            4,
            "if (!current.IsPresent || (object?)current.Value is null) throw new global::System.InvalidOperationException(\"Cannot apply granular collection operations to a missing collection.\");"
        );
        code.AppendLineAt(4, "var source = current.Value!;");
        code.AppendLineAt(
            4,
            "var map = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(4, "foreach (var item in source)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var k = " + KeyOfMethod(member) + "(item);");
        code.AppendLineAt(
            5,
            "if (!map.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(4, "}");
        // Removals.
        code.AppendLineAt(
            4,
            "if (__removed is not null) foreach (var k in __removed) map.Remove(k);"
        );
        // Edits.
        if (hasPatch)
        {
            var elementFragment = ElementFragmentType(member);
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!map.TryGetValue(kv.Key, out var existing)) throw new global::System.InvalidOperationException(\"Cannot edit a missing element.\");"
            );
            code.AppendLineAt(
                5,
                "var applied = kv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(existing)));"
            );
            code.AppendLineAt(
                5,
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLineAt(5, "var updated = applied.Value!.ToModel();");
            code.AppendLineAt(
                5,
                "if (!"
                    + comparer
                    + ".Equals("
                    + KeyOfMethod(member)
                    + "(updated), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(5, "map[kv.Key] = updated;");
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!map.ContainsKey(kv.Key)) throw new global::System.InvalidOperationException(\"Cannot update a missing element.\");"
            );
            code.AppendLineAt(
                5,
                "if (!"
                    + comparer
                    + ".Equals("
                    + KeyOfMethod(member)
                    + "(kv.Value), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an update is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(5, "map[kv.Key] = kv.Value;");
            code.AppendLineAt(4, "}");
        }

        // Adds.
        code.AppendLineAt(4, "if (__added is not null) foreach (var item in __added)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var k = " + KeyOfMethod(member) + "(item);");
        code.AppendLineAt(
            5,
            "if (map.ContainsKey(k)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(5, "map[k] = item;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.List<" + elementType + "> result;"
        );
        code.AppendLineAt(4, "if (__order is not null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__order.Count != map.Count) throw new global::System.InvalidOperationException(\"Order must list exactly the final keys.\");"
        );
        code.AppendLineAt(
            5,
            "result = new global::System.Collections.Generic.List<" + elementType + ">(map.Count);"
        );
        code.AppendLineAt(5, "foreach (var k in __order)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!map.TryGetValue(k, out var item)) throw new global::System.InvalidOperationException(\"Order lists an unknown key.\");"
        );
        code.AppendLineAt(6, "result.Add(item);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // The final key set is exactly map.Keys (source minus removals plus adds),
        // so map.Count is the exact result capacity: no re-enumeration of source
        // via Enumerable.Count. The added loop keeps its duplicate guard but
        // resolves it through an O(1) comparer-correct set instead of the former
        // O(N) result.Exists scan per added element (former O(N x K) behavior).
        // The set is built only when adds exist, so pure edit/remove applies pay
        // nothing extra; for valid patches the guard never fires because Apply
        // already rejected added keys that collide with the map.
        code.AppendLineAt(
            5,
            "result = new global::System.Collections.Generic.List<" + elementType + ">(map.Count);"
        );
        code.AppendLineAt(5, "foreach (var item in source)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "var k = " + KeyOfMethod(member) + "(item);");
        code.AppendLineAt(6, "if (map.TryGetValue(k, out var current2)) result.Add(current2);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__added is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __emitted = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "foreach (var __placed in result) __emitted.Add(" + KeyOfMethod(member) + "(__placed));"
        );
        code.AppendLineAt(
            6,
            "foreach (var item in __added) { var k = "
                + KeyOfMethod(member)
                + "(item); if (__emitted.Add(k)) result.Add(map[k]); }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return " + MaterializeSequence(member, "result") + ";");
        code.AppendLineAt(3, "}");
    }

    internal static string MaterializeSequence(SparseMemberModel member, string variable)
    {
        var runtime = SparseFragmentPatchEmitter.Runtime;
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

    private static string ConvertSequenceToMember(SparseMemberModel member, string variable)
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

    private static void EmitKeyedBetween(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string facade
    )
    {
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var optionalList = runtime + "Optional<" + listType + ">";
        var operation = runtime + "FragmentOperation<" + listType + ">";
        code.AppendLineAt(
            3,
            "public static "
                + patchName
                + " Between("
                + optionalList
                + " before, "
                + optionalList
                + " after)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var patch = new " + patchName + "();");
        code.AppendLineAt(4, "if (before.IsPresent != after.IsPresent)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "patch.__whole = after.IsPresent ? "
                + operation
                + ".Set(after.Value) : "
                + operation
                + ".Unset;"
        );
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (!before.IsPresent) return patch;");
        code.AppendLineAt(
            4,
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;"
        );
        code.AppendLineAt(4, "if ((object?)before.Value is null || (object?)after.Value is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!"
                + facade
                + ".AreEqual((object?)before.Value, (object?)after.Value)) patch.__whole = "
                + operation
                + ".Set(after.Value);"
        );
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if ("
                + facade
                + ".AreEqual((object?)before.Value, (object?)after.Value)) return patch;"
        );
        // Build maps with duplicate detection. Each collection is enumerated exactly
        // once: the before pass collects the map and the order list together, and
        // the added pass reuses afterOrder/afterMap instead of re-enumerating
        // after.Value and recomputing keys. Capacity hints come from a
        // netstandard2.0-safe ICollection/IReadOnlyCollection probe (0 when the
        // member shape exposes no Count); duplicate-key validation and all
        // comparer/key semantics are unchanged.
        code.AppendLineAt(
            4,
            "var __beforeCapacity = before.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __beforeCollection ? __beforeCollection.Count : (before.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __beforeReadOnly ? __beforeReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var beforeMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__beforeCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var beforeOrder = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__beforeCapacity);"
        );
        code.AppendLineAt(4, "foreach (var item in before.Value!)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var k = " + KeyOfMethod(member) + "(item);");
        code.AppendLineAt(
            5,
            "if (!beforeMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(5, "beforeOrder.Add(k);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "var __afterCapacity = after.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __afterCollection ? __afterCollection.Count : (after.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __afterReadOnly ? __afterReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var afterMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__afterCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var afterOrder = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__afterCapacity);"
        );
        code.AppendLineAt(4, "foreach (var item in after.Value!)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var k = " + KeyOfMethod(member) + "(item);");
        code.AppendLineAt(
            5,
            "if (!afterMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(5, "afterOrder.Add(k);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "var removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(beforeMap.Count);"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in beforeMap.Keys) if (!afterMap.ContainsKey(k)) removed.Add(k);"
        );
        // Added entries are derived from afterOrder/afterMap (after-order, no key
        // recomputation, no second enumeration of after.Value). The capacity is
        // the exact upper bound |after| - |before intersect after|.
        code.AppendLineAt(
            4,
            "var added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(global::System.Math.Max(0, afterMap.Count - beforeMap.Count + removed.Count));"
        );
        code.AppendLineAt(4, "foreach (var k in afterOrder)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (!beforeMap.ContainsKey(k)) added.Add(afterMap[k]);");
        code.AppendLineAt(4, "}");
        if (hasPatch)
        {
            var elementFragment = ElementFragmentType(member);
            var elementPatch = ElementPatchType(member);
            var prefix = ElementPatchPrefix(member);
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">? edited = null;"
            );
            code.AppendLineAt(4, "foreach (var k in beforeMap.Keys)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (!afterMap.TryGetValue(k, out var afterItem)) continue;");
            code.AppendLineAt(5, "var beforeItem = beforeMap[k];");
            code.AppendLineAt(
                5,
                "var nested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Between("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(beforeItem)), "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(afterItem)));"
            );
            code.AppendLineAt(
                5,
                "if (!nested.__SparseIsEmpty()) (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = nested;"
            );
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">? edited = null;"
            );
            code.AppendLineAt(4, "foreach (var k in beforeMap.Keys)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (!afterMap.TryGetValue(k, out var afterItem)) continue;");
            code.AppendLineAt(
                5,
                "if (!"
                    + facade
                    + ".AreEqual((object?)beforeMap[k], (object?)afterItem)) (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + "))[k] = afterItem;"
            );
            code.AppendLineAt(4, "}");
        }

        code.AppendLineAt(
            4,
            "if (removed.Count == 0 && added.Count == 0 && (edited is null || edited.Count == 0))"
        );
        code.AppendLineAt(4, "{");
        // Order-only change? Before/after equal as sets but order differs -> still need order patch.
        // beforeOrder was already collected in the single before pass above.
        code.AppendLineAt(
            5,
            "if ("
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(beforeOrder, afterOrder)) return patch;"
        );
        code.AppendLineAt(5, "patch.__order = afterOrder;");
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (removed.Count > 0) patch.__removed = removed;");
        code.AppendLineAt(4, "if (added.Count > 0) patch.__added = added;");
        code.AppendLineAt(
            4,
            "if (edited is not null && edited.Count > 0) patch.__edited = edited;"
        );
        code.AppendLineAt(
            4,
            "if (!"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(beforeOrder, afterOrder)) patch.__order = afterOrder;"
        );
        code.AppendLineAt(4, "return patch;");
        code.AppendLineAt(3, "}");
    }

    private static void EmitKeyedCompose(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer
    )
    {
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var kind = runtime + "FragmentOperationKind";
        var operation = runtime + "FragmentOperation<" + listType + ">";
        code.AppendLineAt(3, "public " + patchName + " Compose(" + patchName + " next)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(4, "if (next.__whole.Kind != " + kind + ".Unchanged)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "result.__whole = next.__whole;");
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__whole.Kind != " + kind + ".Unchanged)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + kind
                + ".Unset) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Unset.\");"
        );
        code.AppendLineAt(
            5,
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + listType
                + ">.Present(__whole.Value));"
        );
        code.AppendLineAt(5, "result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
        // Granular + granular: merge key operation logs preserving sequential semantics.
        // Added keys.
        code.AppendLineAt(
            4,
            "var thisAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (__added is not null) foreach (var item in __added) thisAdded["
                + KeyOfMethod(member)
                + "(item)] = item;"
        );
        code.AppendLineAt(
            4,
            "var nextAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (next.__added is not null) foreach (var item in next.__added) nextAdded["
                + KeyOfMethod(member)
                + "(item)] = item;"
        );
        code.AppendLineAt(
            4,
            "var thisRemoved = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(("
                + "global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)"
                + "(__removed ?? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)new "
                + keyType
                + "[0]), "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var nextRemoved = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(("
                + "global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)"
                + "(next.__removed ?? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)new "
                + keyType
                + "[0]), "
                + comparer
                + ");"
        );
        // Net removals: removed by either, minus re-added later. netRemoved keeps
        // insertion order (this removals, then next removals) while
        // __netRemovedSet gives O(1) comparer-correct dedup instead of the
        // former O(K^2) Enumerable.Contains scan per candidate key.
        code.AppendLineAt(
            4,
            "var netRemoved = new global::System.Collections.Generic.List<"
                + keyType
                + ">(thisRemoved.Count + nextRemoved.Count);"
        );
        code.AppendLineAt(
            4,
            "var __netRemovedSet = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in thisRemoved) if (!nextAdded.ContainsKey(k) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in nextRemoved) if ((!thisAdded.ContainsKey(k) || thisRemoved.Contains(k)) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        // Net adds: this-added surviving next-removal (with next edits applied), plus next-added.
        code.AppendLineAt(
            4,
            "var netAdded = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        if (hasPatch)
        {
            var elementFragment = ElementFragmentType(member);
            code.AppendLineAt(4, "foreach (var kv in thisAdded)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var nextEdit))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var applied = nextEdit.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(kv.Value)));"
            );
            code.AppendLineAt(
                6,
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLineAt(
                6,
                "if (!"
                    + comparer
                    + ".Equals("
                    + KeyOfMethod(member)
                    + "(applied.Value!.ToModel()), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(6, "netAdded.Add(applied.Value!.ToModel());");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else netAdded.Add(kv.Value);");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + ElementPatchType(member)
                    + ">? netEdited = null;"
            );
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(5, "if (nextAdded.ContainsKey(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var n2) && !n2.__SparseIsEmpty()) { var __c = kv.Value.Compose(n2); if (__c.__SparseIsEmpty()) { netEdited?.Remove(kv.Key); } else (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = __c; }"
            );
            code.AppendLineAt(
                5,
                "else if ((next.__edited is null || !next.__edited.ContainsKey(kv.Key)) && !kv.Value.__SparseIsEmpty()) (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited)"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (thisAdded.ContainsKey(kv.Key) || thisRemoved.Contains(kv.Key)) continue;"
            );
            code.AppendLineAt(5, "if (kv.Value.__SparseIsEmpty()) continue;");
            code.AppendLineAt(
                5,
                "if (netEdited is not null && netEdited.ContainsKey(kv.Key)) continue;"
            );
            code.AppendLineAt(
                5,
                "(netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLineAt(4, "if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLineAt(
                4,
                "if (netEdited is not null && netEdited.Count > 0) result.__edited = netEdited;"
            );
        }
        else
        {
            code.AppendLineAt(4, "foreach (var kv in thisAdded)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var upd)) netAdded.Add(upd); else netAdded.Add(kv.Value);"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLineAt(
                4,
                "var netEdited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) if (!nextRemoved.Contains(kv.Key) && !nextAdded.ContainsKey(kv.Key)) netEdited[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited) { if (thisAdded.ContainsKey(kv.Key) || thisRemoved.Contains(kv.Key)) continue; netEdited[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(4, "if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLineAt(4, "if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLineAt(4, "if (netEdited.Count > 0) result.__edited = netEdited;");
        }

        // Order: next order wins when present (filtered to net keys), else this order filtered.
        code.AppendLineAt(4, "if (next.__order is not null && next.__order.Count > 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __nextOrder = new global::System.Collections.Generic.List<"
                + keyType
                + ">(); foreach (var k in next.__order) if (!netRemoved.Contains(k)) __nextOrder.Add(k);"
        );
        code.AppendLineAt(
            5,
            "if (next.__added is not null) foreach (var item in next.__added) { var __ak = "
                + KeyOfMethod(member)
                + "(item); if (!__nextOrder.Contains(__ak, "
                + comparer
                + ")) __nextOrder.Add(__ak); }"
        );
        code.AppendLineAt(5, "if (__nextOrder.Count > 0) result.__order = __nextOrder;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__order is not null && __order.Count > 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var order = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            5,
            "foreach (var k in __order) if (!nextRemoved.Contains(k)) order.Add(k);"
        );
        code.AppendLineAt(
            5,
            "if (next.__added is not null) foreach (var item in next.__added) order.Add("
                + KeyOfMethod(member)
                + "(item));"
        );
        code.AppendLineAt(5, "if (order.Count > 0) result.__order = order;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "public static "
                + patchName
                + " Compose("
                + patchName
                + " first, "
                + patchName
                + " second) { if (first is null) throw new global::System.ArgumentNullException(nameof(first)); return first.Compose(second); }"
        );
    }

    private static void EmitKeyedRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string facade
    )
    {
        // Placeholder: full keyed rebase emitted in follow-up edit (kept small here to land the type first).
        code.AppendLineAt(
            3,
            "public static global::SparseFragments.RebaseResult<"
                + patchName
                + "> Rebase("
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<"
                + listType
                + "> baseState, "
                + patchName
                + " local, "
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<"
                + listType
                + "> currentState)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (local is null) throw new global::System.ArgumentNullException(nameof(local));"
        );
        code.AppendLineAt(
            4,
            "if (local.__SparseIsEmpty()) return global::SparseFragments.RebaseResult<"
                + patchName
                + ">.Success(new "
                + patchName
                + "());"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(
            4,
            "var conflicts = new global::System.Collections.Generic.List<global::SparseFragments.SparsePatchConflict>();"
        );
        code.AppendLineAt(
            4,
            SparseFragmentPatchEmitter.Runtime + "Optional<" + listType + "> desired;"
        );
        code.AppendLineAt(4, "try { desired = local.Apply(baseState); }");
        code.AppendLineAt(
            4,
            "catch (global::System.InvalidOperationException ex) { conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[0], global::SparseFragments.SparsePatchConflictKind.Nested, "
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<object?>.Missing, "
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<object?>.Missing, "
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<object?>.Missing, ex.Message)); return new global::SparseFragments.RebaseResult<"
                + patchName
                + ">(result, conflicts); }"
        );
        code.AppendLineAt(
            4,
            "if (local.__whole.Kind != "
                + SparseFragmentPatchEmitter.Runtime
                + "FragmentOperationKind.Unchanged)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if ("
                + facade
                + ".AreEqual((object?)baseState.Value, (object?)currentState.Value)) { result.__whole = local.__whole; return new global::SparseFragments.RebaseResult<"
                + patchName
                + ">(result, conflicts); }"
        );
        code.AppendLineAt(
            5,
            "var desiredState = desired; if ("
                + facade
                + ".AreEqual((object?)desiredState.Value, (object?)currentState.Value)) return global::SparseFragments.RebaseResult<"
                + patchName
                + ">.Success(new "
                + patchName
                + "());"
        );
        code.AppendLineAt(
            5,
            "conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[0], global::SparseFragments.SparsePatchConflictKind.Nested, "
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<object?>.Missing, "
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<object?>.Missing, "
                + SparseFragmentPatchEmitter.Runtime
                + "Optional<object?>.Missing, \"The whole collection conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(
            5,
            "return new global::SparseFragments.RebaseResult<" + patchName + ">(result, conflicts);"
        );
        code.AppendLineAt(4, "}");
        // Granular: if base == current keep local; if desired == current drop; else per-key merge with nested rebase where possible.
        // NOTE: desired is computed defensively below (missing/duplicate base yields a conflict, not a throw).
        code.AppendLineAt(
            4,
            "if ("
                + facade
                + ".AreEqual((object?)baseState.Value, (object?)currentState.Value)) { result.__added = local.__added is null ? null : new global::System.Collections.Generic.List<"
                + elementType
                + ">(local.__added); result.__removed = local.__removed is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__removed); result.__edited = local.__edited is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + (hasPatch ? ElementPatchType(member) : elementType)
                + ">(local.__edited, "
                + comparer
                + "); result.__order = local.__order is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__order); return new global::SparseFragments.RebaseResult<"
                + patchName
                + ">(result, conflicts); }"
        );
        code.AppendLineAt(
            4,
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreEqual((object?)desired.Value, (object?)currentState.Value)) return global::SparseFragments.RebaseResult<"
                + patchName
                + ">.Success(new "
                + patchName
                + "());"
        );
        EmitKeyedRebasePerKey(code, member, elementType, keyType, hasPatch, comparer, facade);
        code.AppendLineAt(
            4,
            "return new global::SparseFragments.RebaseResult<" + patchName + ">(result, conflicts);"
        );
        code.AppendLineAt(3, "}");
    }

    private static void EmitKeyedRebasePerKey(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        string keyType,
        bool hasPatch,
        string comparer,
        string facade
    )
    {
        var runtime = SparseFragmentPatchEmitter.Runtime;
        // Build maps. Capacity hints use a netstandard2.0-safe
        // ICollection/IReadOnlyCollection probe (0 when the member shape
        // exposes no Count); duplicate-key validation is unchanged.
        code.AppendLineAt(
            4,
            "var __baseCapacity = baseState.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __baseCollection ? __baseCollection.Count : (baseState.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __baseReadOnly ? __baseReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var baseMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__baseCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) { var k = "
                + KeyOfMethod(member)
                + "(item); if (!baseMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); }"
        );
        code.AppendLineAt(
            4,
            "var __currentCapacity = currentState.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __currentCollection ? __currentCollection.Count : (currentState.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __currentReadOnly ? __currentReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var currentMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__currentCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) { var k = "
                + KeyOfMethod(member)
                + "(item); if (!currentMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); }"
        );
        code.AppendLineAt(
            4,
            "var __desiredCapacity = desired.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __desiredCollection ? __desiredCollection.Count : (desired.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __desiredReadOnly ? __desiredReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var desiredMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__desiredCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) { var k = "
                + KeyOfMethod(member)
                + "(item); if (!desiredMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); }"
        );
        // Pre-index locally touched keys once (O(K)) so the per-key loop below
        // resolves touches with O(1) comparer-correct lookups instead of O(K)
        // List.Exists scans per union key (former O(N x K) behavior). For valid
        // patches (unique added/removed keys, as enforced by Add/Remove and by
        // Between/Compose construction) the maps below resolve exactly the same
        // keys as the scans did.
        code.AppendLineAt(
            4,
            "var __touchedAdded = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var __addedByKey = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">((local.__added is null) ? 0 : local.__added.Count, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (local.__added is not null) foreach (var __touchedItem in local.__added) { var __touchedKey = "
                + KeyOfMethod(member)
                + "(__touchedItem); __touchedAdded.Add(__touchedKey); __addedByKey[__touchedKey] = __touchedItem; }"
        );
        code.AppendLineAt(
            4,
            "var __touchedRemoved = (local.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__removed, "
                + comparer
                + ");"
        );
        // Seed from edited keys for an exact capacity hint, then merge other local touches.
        // Removing keys while scanning base/current/desired preserves first-occurrence
        // key identity and conflict order without allocating an O(N) union.
        code.AppendLineAt(
            4,
            "var __pendingKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__edited is null ? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)__touchedAdded : local.__edited.Keys, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (local.__edited is not null) __pendingKeys.UnionWith(__touchedAdded);"
        );
        code.AppendLineAt(4, "__pendingKeys.UnionWith(__touchedRemoved);");
        if (hasPatch)
        {
            var elementPatch = ElementPatchType(member);
            var elementFragment = ElementFragmentType(member);
            var prefix = ElementPatchPrefix(member);
            AppendPendingKeyLoopStart(code);
            code.AppendLineAt(
                5,
                "var inBase = baseMap.TryGetValue(k, out var b); var inCurrent = currentMap.TryGetValue(k, out var c); var inDesired = desiredMap.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqualsCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual((object?)b, (object?)c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqualsCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual((object?)d, (object?)c));"
            );
            code.AppendLineAt(5, "if (baseEqualsCurrent)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (__touchedRemoved.Contains(k)) { (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); }"
            );
            code.AppendLineAt(
                6,
                "else if (__addedByKey.TryGetValue(k, out var __rebasedAdded)) (result.__added ??= new global::System.Collections.Generic.List<"
                    + elementType
                    + ">()).Add(__rebasedAdded);"
            );
            code.AppendLineAt(
                6,
                "if (local.__edited is not null && local.__edited.TryGetValue(k, out var edit) && !edit.__SparseIsEmpty()) { (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = edit; }"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else if (desiredEqualsCurrent) { }");
            code.AppendLineAt(
                5,
                "else if (inBase && inCurrent && inDesired && local.__edited is not null && local.__edited.TryGetValue(k, out var localEdit))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var nested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Rebase("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(b!)), localEdit, "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(c!)));"
            );
            code.AppendLineAt(
                6,
                "if (!nested.HasConflicts && !nested.Patch.__SparseIsEmpty()) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = nested.Patch;"
            );
            code.AppendLineAt(
                6,
                "else foreach (var nc in nested.Conflicts) conflicts.Add(nc.WithPathPrefix(((object?)k)?.ToString() ?? \"<null>\"));"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, global::SparseFragments.SparsePatchConflictKind.Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The keyed element conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(5, "}");
            AppendPendingKeyLoopEnd(code);
            // Order: keep local order only when current order unchanged and no order conflict.
            code.AppendLineAt(4, "if (local.__order is not null && local.__order.Count > 0)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var baseOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) baseOrder.Add("
                    + KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var currentOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) currentOrder.Add("
                    + KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var desiredOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) desiredOrder.Add("
                    + KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "if ("
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(baseOrder, currentOrder)) result.__order = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(local.__order);"
            );
            code.AppendLineAt(
                5,
                "else if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(desiredOrder, currentOrder)) conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { \""
                    + "order\" }, global::SparseFragments.SparsePatchConflictKind.Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)baseOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)desiredOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)currentOrder), \"The collection order conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(4, "}");
        }
        else
        {
            AppendPendingKeyLoopStart(code);
            code.AppendLineAt(
                5,
                "var inBase = baseMap.TryGetValue(k, out var b); var inCurrent = currentMap.TryGetValue(k, out var c); var inDesired = desiredMap.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqualsCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual((object?)b, (object?)c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqualsCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual((object?)d, (object?)c));"
            );
            code.AppendLineAt(5, "if (baseEqualsCurrent)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (__touchedRemoved.Contains(k)) { (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); }"
            );
            code.AppendLineAt(
                6,
                "else if (__addedByKey.TryGetValue(k, out var __rebasedAdded)) (result.__added ??= new global::System.Collections.Generic.List<"
                    + elementType
                    + ">()).Add(__rebasedAdded);"
            );
            code.AppendLineAt(
                6,
                "else if (local.__edited is not null && local.__edited.TryGetValue(k, out var upd)) { (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + "))[k] = upd; }"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else if (!desiredEqualsCurrent)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, global::SparseFragments.SparsePatchConflictKind.Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The keyed element conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(5, "}");
            AppendPendingKeyLoopEnd(code);
            code.AppendLineAt(4, "if (local.__order is not null && local.__order.Count > 0)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var baseOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) baseOrder.Add("
                    + KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var currentOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) currentOrder.Add("
                    + KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var desiredOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) desiredOrder.Add("
                    + KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "if ("
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(baseOrder, currentOrder)) result.__order = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(local.__order);"
            );
            code.AppendLineAt(
                5,
                "else if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(desiredOrder, currentOrder)) conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { \""
                    + "order\" }, global::SparseFragments.SparsePatchConflictKind.Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)baseOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)desiredOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)currentOrder), \"The collection order conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(4, "}");
        }
    }

    private static void EmitDictionaryPatch(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var patchName = CollectionPatchName(member);
        var keyType = KeyType(member);
        var valueType = DictionaryValueType(member);
        var dictType = DictionaryType(member);
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var operation = runtime + "FragmentOperation<" + dictType + ">";
        var kind = runtime + "FragmentOperationKind";
        var optionalDict = runtime + "Optional<" + dictType + ">";
        var facade = "global::SparseFragments.CompilerServices.SparseFragmentRuntime";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var hasPatch = HasValuePatch(member);
        var editedValueType = hasPatch ? ValuePatchType(member) : valueType;

        code.AppendLineAt(
            2,
            "/// <summary>Dictionary patch for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLineAt(2, "public sealed class " + patchName);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "private " + operation + " __whole;");
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">? __set;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.List<" + keyType + ">? __removed;"
        );
        code.AppendLineAt(
            3,
            hasPatch
                ? "private global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + editedValueType
                    + ">? __edited;"
                : "private global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">? __edited;"
        );
        code.AppendLineAt(3, "public bool IsEmpty => __whole.Kind == " + kind + ".Unchanged");
        code.AppendLineAt(
            4,
            "&& (__set is null || __set.Count == 0) && (__removed is null || __removed.Count == 0) && (__edited is null || __edited.Count == 0);"
        );
        code.AppendLineAt(3, "internal bool __SparseIsEmpty() => IsEmpty;");
        code.AppendLineAt(3, "public void Set(" + dictType + " value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "__whole = "
                + operation
                + ".Set(value); __set = null; __removed = null; __edited = null;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public void Unset()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "__whole = " + operation + ".Unset; __set = null; __removed = null; __edited = null;"
        );
        code.AppendLineAt(3, "}");
        if (!IsInterfaceMember(member))
        {
            code.AppendLineAt(
                3,
                "public static implicit operator "
                    + patchName
                    + "("
                    + dictType
                    + " value) => new "
                    + patchName
                    + " { __whole = "
                    + operation
                    + ".Set(value) };"
            );
        }
        code.AppendLineAt(3, "private void EnsureGranular(string operation)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__whole.Kind != "
                + kind
                + ".Unchanged) throw new global::System.InvalidOperationException(\"Cannot apply '\" + operation + \"' when the whole dictionary is set.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public void SetEntry(" + keyType + " key, " + valueType + " value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "EnsureGranular(\"SetEntry\");");
        code.AppendLineAt(
            4,
            "if (__removed is not null) __removed.RemoveAll(k => " + comparer + ".Equals(k, key));"
        );
        code.AppendLineAt(4, "if (__edited is not null) __edited.Remove(key);");
        code.AppendLineAt(
            4,
            "__set ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(4, "__set[key] = value;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public void RemoveEntry(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "EnsureGranular(\"RemoveEntry\");");
        code.AppendLineAt(4, "if (__set is not null) { if (__set.Remove(key)) return; }");
        code.AppendLineAt(4, "if (__edited is not null) __edited.Remove(key);");
        code.AppendLineAt(
            4,
            "__removed ??= new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            4,
            "foreach (var existing in __removed) if ("
                + comparer
                + ".Equals(existing, key)) return;"
        );
        code.AppendLineAt(4, "__removed.Add(key);");
        code.AppendLineAt(3, "}");
        if (hasPatch)
        {
            var valuePatch = ValuePatchType(member);
            code.AppendLineAt(3, "public " + valuePatch + " Edit(" + keyType + " key)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "EnsureGranular(\"Edit\");");
            code.AppendLineAt(
                4,
                "if (__removed is not null) foreach (var r in __removed) if ("
                    + comparer
                    + ".Equals(r, key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed entry.\");"
            );
            code.AppendLineAt(
                4,
                "if (__set is not null && __set.ContainsKey(key)) throw new global::System.InvalidOperationException(\"Cannot edit an entry set in the same patch. Update the set value directly.\");"
            );
            code.AppendLineAt(
                4,
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (!__edited.TryGetValue(key, out var patch)) { patch = new "
                    + valuePatch
                    + "(); __edited[key] = patch; }"
            );
            code.AppendLineAt(4, "return patch;");
            code.AppendLineAt(3, "}");
        }
        else
        {
            code.AppendLineAt(
                3,
                "public void UpdateEntry(" + keyType + " key, " + valueType + " value)"
            );
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "EnsureGranular(\"UpdateEntry\");");
            code.AppendLineAt(
                4,
                "if (__removed is not null) foreach (var r in __removed) if ("
                    + comparer
                    + ".Equals(r, key)) throw new global::System.InvalidOperationException(\"Cannot update a removed entry. Set it again instead.\");"
            );
            code.AppendLineAt(
                4,
                "if (__set is not null && __set.ContainsKey(key)) { __set[key] = value; return; }"
            );
            code.AppendLineAt(
                4,
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(4, "__edited[key] = value;");
            code.AppendLineAt(3, "}");
        }
        if (hasPatch)
        {
            code.AppendLineAt(
                3,
                "internal void __SparseSetEdited("
                    + keyType
                    + " key, "
                    + ValuePatchType(member)
                    + " patch)"
            );
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "EnsureGranular(\"SetEdited\");");
            code.AppendLineAt(
                4,
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            code.AppendLineAt(
                4,
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + ValuePatchType(member)
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(4, "__edited[key] = patch;");
            code.AppendLineAt(3, "}");
        }

        // Apply.
        code.AppendLineAt(3, "public " + optionalDict + " Apply(" + optionalDict + " current)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__whole.Kind != " + kind + ".Unchanged) return __whole.Apply(current);"
        );
        code.AppendLineAt(4, "if (IsEmpty) return current;");
        code.AppendLineAt(
            4,
            "if (!current.IsPresent || (object?)current.Value is null) throw new global::System.InvalidOperationException(\"Cannot apply granular dictionary operations to a missing dictionary.\");"
        );
        code.AppendLineAt(
            4,
            "var result = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(("
                + "global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">"
                + ")current.Value!, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null) foreach (var k in __removed) result.Remove(k);"
        );
        if (hasPatch)
        {
            var valueFragment = ValueFragmentType(member);
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!result.TryGetValue(kv.Key, out var existing)) throw new global::System.InvalidOperationException(\"Cannot edit a missing entry.\");"
            );
            code.AppendLineAt(
                5,
                "var applied = kv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(existing)));"
            );
            code.AppendLineAt(
                5,
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Entry edit removed the entry. Use RemoveEntry instead.\");"
            );
            code.AppendLineAt(5, "result[kv.Key] = applied.Value!.ToModel();");
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) { if (!result.ContainsKey(kv.Key)) throw new global::System.InvalidOperationException(\"Cannot update a missing entry.\"); result[kv.Key] = kv.Value; }"
            );
        }

        code.AppendLineAt(
            4,
            "if (__set is not null) foreach (var kv in __set) result[kv.Key] = kv.Value;"
        );
        code.AppendLineAt(
            4,
            "return "
                + runtime
                + "Optional<"
                + dictType
                + ">.Present("
                + ConvertDictionaryToMember(member, "result")
                + ");"
        );
        code.AppendLineAt(3, "}");
        // Between.
        code.AppendLineAt(
            3,
            "public static "
                + patchName
                + " Between("
                + optionalDict
                + " before, "
                + optionalDict
                + " after)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var patch = new " + patchName + "();");
        code.AppendLineAt(4, "if (before.IsPresent != after.IsPresent)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "patch.__whole = after.IsPresent ? "
                + operation
                + ".Set(after.Value) : "
                + operation
                + ".Unset;"
        );
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (!before.IsPresent) return patch;");
        code.AppendLineAt(
            4,
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;"
        );
        code.AppendLineAt(4, "if ((object?)before.Value is null || (object?)after.Value is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!"
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(before.Value, after.Value)) patch.__whole = "
                + operation
                + ".Set(after.Value);"
        );
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(before.Value, after.Value)) return patch;"
        );
        code.AppendLineAt(
            4,
            "var beforeDict = before.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __beforeDirect && "
                + "global::System.Object.Equals(__beforeDirect.Comparer, "
                + comparer
                + ") ? __beforeDirect : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(("
                + "global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)before.Value!, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var afterDict = after.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __afterDirect && "
                + "global::System.Object.Equals(__afterDirect.Comparer, "
                + comparer
                + ") ? __afterDirect : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(("
                + "global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)after.Value!, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in beforeDict.Keys) if (!afterDict.ContainsKey(k)) (patch.__removed ??= new global::System.Collections.Generic.List<"
                + keyType
                + ">()).Add(k);"
        );
        if (hasPatch)
        {
            var valueFragment = ValueFragmentType(member);
            var valuePatch = ValuePatchType(member);
            var prefix = ValuePatchPrefix(member);
            code.AppendLineAt(4, "foreach (var kv in afterDict)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!beforeDict.TryGetValue(kv.Key, out var b)) { (patch.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value; continue; }"
            );
            code.AppendLineAt(
                5,
                "var nested = "
                    + valuePatch
                    + "."
                    + prefix
                    + "Between("
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(b!)), "
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(kv.Value)));"
            );
            code.AppendLineAt(
                5,
                "if (!nested.__SparseIsEmpty()) (patch.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[kv.Key] = nested;"
            );
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(4, "foreach (var kv in afterDict)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!beforeDict.TryGetValue(kv.Key, out var b)) { (patch.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value; continue; }"
            );
            code.AppendLineAt(
                5,
                "if (!"
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, kv.Value)) (patch.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "}");
        }

        code.AppendLineAt(4, "return patch;");
        code.AppendLineAt(3, "}");
        // Compose.
        code.AppendLineAt(3, "public " + patchName + " Compose(" + patchName + " next)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(
            4,
            "if (next.__whole.Kind != "
                + kind
                + ".Unchanged) { result.__whole = next.__whole; return result; }"
        );
        code.AppendLineAt(4, "if (__whole.Kind != " + kind + ".Unchanged)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + kind
                + ".Unset) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Unset.\");"
        );
        code.AppendLineAt(
            4,
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + dictType
                + ">.Present(__whole.Value));"
        );
        code.AppendLineAt(4, "result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "var removed = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null) foreach (var k in __removed) removed.Add(k);"
        );
        code.AppendLineAt(
            4,
            "if (next.__removed is not null) foreach (var k in next.__removed) removed.Add(k);"
        );
        code.AppendLineAt(
            4,
            "var set = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (__set is not null) foreach (var kv in __set) set[kv.Key] = kv.Value;"
        );
        code.AppendLineAt(
            4,
            "if (next.__set is not null) foreach (var kv in next.__set) { set[kv.Key] = kv.Value; removed.Remove(kv.Key); }"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null) foreach (var k in __removed) if (next.__set is not null && next.__set.ContainsKey(k)) removed.Remove(k);"
        );
        // Remove keys that are set by this then removed by next? Already: removed contains next removed, set does not contain them unless re-set. Need to drop this-set keys removed by next:
        code.AppendLineAt(
            4,
            "if (next.__removed is not null) foreach (var k in next.__removed) set.Remove(k);"
        );
        // Pre-index next removals once so per-edit checks below are O(1)
        // comparer-correct lookups instead of O(K) List.Contains scans.
        code.AppendLineAt(
            4,
            "var __nextRemoved = (next.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(next.__removed, "
                + comparer
                + ");"
        );
        if (hasPatch)
        {
            var valuePatch = ValuePatchType(member);
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">? edited = null;"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) { if (kv.Value.__SparseIsEmpty()) continue; if (__nextRemoved.Contains(kv.Key)) continue; edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "); edited[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited) { if (kv.Value.__SparseIsEmpty()) continue; if (set.TryGetValue(kv.Key, out var setBase)) { var __folded = kv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + ValueFragmentType(member)
                    + "?>.Present("
                    + ValueFragmentType(member)
                    + ".From(setBase))); if (__folded.IsPresent && __folded.Value is not null) set[kv.Key] = __folded.Value!.ToModel(); continue; } if (edited is not null && edited.TryGetValue(kv.Key, out var first)) { var __composed = first.Compose(kv.Value); if (__composed.__SparseIsEmpty()) edited.Remove(kv.Key); else edited[kv.Key] = __composed; } else (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(
                4,
                "if (edited is not null && edited.Count > 0) result.__edited = edited;"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "var edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) edited[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited) edited[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "foreach (var k in removed) { edited.Remove(k); set.Remove(k); }");
            code.AppendLineAt(4, "foreach (var k in set.Keys) edited.Remove(k);");
            code.AppendLineAt(4, "if (edited.Count > 0) result.__edited = edited;");
        }

        code.AppendLineAt(
            4,
            "if (removed.Count > 0) result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(removed);"
        );
        code.AppendLineAt(4, "if (set.Count > 0) result.__set = set;");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "public static "
                + patchName
                + " Compose("
                + patchName
                + " first, "
                + patchName
                + " second) { if (first is null) throw new global::System.ArgumentNullException(nameof(first)); return first.Compose(second); }"
        );
        code.AppendLineAt(
            3,
            "public "
                + patchName
                + " Invert("
                + optionalDict
                + " baseline) => Between(Apply(baseline), baseline);"
        );
        // Rebase (simplified key-wise).
        code.AppendLineAt(
            3,
            "public static global::SparseFragments.RebaseResult<"
                + patchName
                + "> Rebase("
                + optionalDict
                + " baseState, "
                + patchName
                + " local, "
                + optionalDict
                + " currentState)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (local is null) throw new global::System.ArgumentNullException(nameof(local));"
        );
        code.AppendLineAt(
            4,
            "if (local.__SparseIsEmpty()) return global::SparseFragments.RebaseResult<"
                + patchName
                + ">.Success(new "
                + patchName
                + "());"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(
            4,
            "var conflicts = new global::System.Collections.Generic.List<global::SparseFragments.SparsePatchConflict>();"
        );
        code.AppendLineAt(4, optionalDict + " desired;");
        code.AppendLineAt(4, "try { desired = local.Apply(baseState); }");
        code.AppendLineAt(
            4,
            "catch (global::System.InvalidOperationException ex) { conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[0], global::SparseFragments.SparsePatchConflictKind.Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, ex.Message)); return new global::SparseFragments.RebaseResult<"
                + patchName
                + ">(result, conflicts); }"
        );
        code.AppendLineAt(4, "if (local.__whole.Kind != " + kind + ".Unchanged)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(baseState.Value, currentState.Value)) { result.__whole = local.__whole; return new global::SparseFragments.RebaseResult<"
                + patchName
                + ">(result, conflicts); }"
        );
        code.AppendLineAt(
            5,
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(desired.Value, currentState.Value)) return global::SparseFragments.RebaseResult<"
                + patchName
                + ">.Success(new "
                + patchName
                + "());"
        );
        code.AppendLineAt(
            5,
            "conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[0], global::SparseFragments.SparsePatchConflictKind.Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, \"The whole dictionary conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(
            5,
            "return new global::SparseFragments.RebaseResult<" + patchName + ">(result, conflicts);"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(baseState.Value, currentState.Value)) { result.__set = local.__set is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(local.__set, "
                + comparer
                + "); result.__removed = local.__removed is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__removed); result.__edited = local.__edited is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedValueType
                + ">(local.__edited, "
                + comparer
                + "); return new global::SparseFragments.RebaseResult<"
                + patchName
                + ">(result, conflicts); }"
        );
        code.AppendLineAt(
            4,
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(desired.Value, currentState.Value)) return global::SparseFragments.RebaseResult<"
                + patchName
                + ">.Success(new "
                + patchName
                + "());"
        );
        // Per-key three-way (without nested rebase composition for brevity: nested edits rebase recursively when both edited).
        EmitRebaseDictionarySnapshot(code, "baseState", "baseDict", keyType, valueType, comparer);
        EmitRebaseDictionarySnapshot(
            code,
            "currentState",
            "currentDict",
            keyType,
            valueType,
            comparer
        );
        EmitRebaseDictionarySnapshot(code, "desired", "desiredDict", keyType, valueType, comparer);
        // Pre-index locally removed keys once (O(K)) so per-key touch checks are
        // O(1) comparer-correct lookups instead of O(K) List.Contains scans.
        code.AppendLineAt(
            4,
            "var __dictRemoved = (local.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__removed, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var __pendingKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__set is not null ? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)local.__set.Keys : (local.__edited is not null ? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)local.__edited.Keys : __dictRemoved), "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (local.__set is not null && local.__edited is not null) __pendingKeys.UnionWith(local.__edited.Keys);"
        );
        code.AppendLineAt(4, "__pendingKeys.UnionWith(__dictRemoved);");
        if (hasPatch)
        {
            var valuePatch = ValuePatchType(member);
            var valueFragment = ValueFragmentType(member);
            var prefix = ValuePatchPrefix(member);
            AppendPendingKeyLoopStart(code, "baseDict", "currentDict", "desiredDict");
            code.AppendLineAt(
                5,
                "var inBase = baseDict.TryGetValue(k, out var b); var inCurrent = currentDict.TryGetValue(k, out var c); var inDesired = desiredDict.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "var touchesSet = local.__set is not null && local.__set.ContainsKey(k); var touchesRemoved = __dictRemoved.Contains(k); var touchesEdited = local.__edited is not null && local.__edited.ContainsKey(k);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(d, c));"
            );
            code.AppendLineAt(
                5,
                "if (baseEqCurrent) { if (touchesSet) (result.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = local.__set![k]; else if (touchesRemoved) (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); else (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[k] = local.__edited![k]; continue; }"
            );
            code.AppendLineAt(5, "if (desiredEqCurrent) continue;");
            code.AppendLineAt(5, "if (touchesEdited && inBase && inCurrent && inDesired)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var nested = "
                    + valuePatch
                    + "."
                    + prefix
                    + "Rebase("
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(b!)), local.__edited![k], "
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(c!)));"
            );
            code.AppendLineAt(
                6,
                "if (!nested.HasConflicts && !nested.Patch.__SparseIsEmpty()) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[k] = nested.Patch; else foreach (var nc in nested.Conflicts) conflicts.Add(nc.WithPathPrefix(((object?)k)?.ToString() ?? \"<null>\"));"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "else conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, global::SparseFragments.SparsePatchConflictKind.Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The dictionary entry conflicts with a concurrent change.\"));"
            );
            AppendPendingKeyLoopEnd(code);
        }
        else
        {
            AppendPendingKeyLoopStart(code, "baseDict", "currentDict", "desiredDict");
            code.AppendLineAt(
                5,
                "var inBase = baseDict.TryGetValue(k, out var b); var inCurrent = currentDict.TryGetValue(k, out var c); var inDesired = desiredDict.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(d, c));"
            );
            code.AppendLineAt(
                5,
                "if (baseEqCurrent) { if (local.__set is not null && local.__set.TryGetValue(k, out var sv)) (result.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = sv; else if (__dictRemoved.Contains(k)) (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); else if (local.__edited is not null && local.__edited.TryGetValue(k, out var ev)) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = ev; continue; }"
            );
            code.AppendLineAt(
                5,
                "if (!desiredEqCurrent) conflicts.Add(new global::SparseFragments.SparsePatchConflict(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, global::SparseFragments.SparsePatchConflictKind.Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The dictionary entry conflicts with a concurrent change.\"));"
            );
            AppendPendingKeyLoopEnd(code);
        }

        code.AppendLineAt(
            4,
            "return new global::SparseFragments.RebaseResult<" + patchName + ">(result, conflicts);"
        );
        code.AppendLineAt(3, "}");
        SparsePatchStjEmitter.AppendDictionaryStj(code, member);
        code.AppendLineAt(2, "}");
    }

    private static void EmitRebaseDictionarySnapshot(
        SharedIndentedBuilder code,
        string state,
        string variable,
        string keyType,
        string valueType,
        string comparer
    )
    {
        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
        var interfaceType =
            $"global::System.Collections.Generic.IDictionary<{keyType}, {valueType}>";
        var direct = "__" + variable + "Direct";
        // These indexes are read-only. Keep fallback assignment semantics for custom comparers.
        code.AppendLineAt(
            4,
            $"var {variable} = {state}.IsPresent && {state}.Value is {dictionaryType} {direct} && global::System.Object.Equals({direct}.Comparer, {comparer}) ? {direct} : null;"
        );
        code.AppendLineAt(4, $"if ({variable} is null)");
        code.AppendLineAt(4, "{");
        var source = "__" + variable + "Source";
        code.AppendLineAt(
            5,
            $"var {source} = {state}.IsPresent ? ({interfaceType}?){state}.Value : null;"
        );
        code.AppendLineAt(
            5,
            $"{variable} = new {dictionaryType}({source}?.Count ?? 0, {comparer});"
        );
        code.AppendLineAt(
            5,
            $"if ({source} is not null) foreach (var kv in {source}) {variable}[kv.Key] = kv.Value;"
        );
        code.AppendLineAt(4, "}");
    }

    private static string ConvertDictionaryToMember(SparseMemberModel member, string variable)
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
