namespace SparseFragments.Generator.Shared;

/// <summary>Emits the dictionary patch surface plus Apply and Between algebra.</summary>
internal static class SparseDictionaryPatchEmitter
{
    internal static void EmitDictionaryPatch(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        var keyType = SparseKeyedCollectionEmitter.KeyType(member);
        var valueType = SparseKeyedCollectionEmitter.DictionaryValueType(member);
        var dictType = SparseKeyedCollectionEmitter.DictionaryType(member);
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var operation = runtime + "FragmentOperation<" + dictType + ">";
        var kind = runtime + "FragmentOperationKind";
        var optionalDict = runtime + "Optional<" + dictType + ">";
        var facade = "global::SparseFragments.CompilerServices.SparseFragmentRuntime";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var hasPatch = SparseKeyedCollectionEmitter.HasValuePatch(member);
        var editedValueType = hasPatch
            ? SparseKeyedCollectionEmitter.ValuePatchType(member)
            : valueType;

        EmitDictionarySurface(
            code,
            member,
            patchName,
            keyType,
            valueType,
            dictType,
            runtime,
            operation,
            kind,
            comparer,
            hasPatch,
            editedValueType
        );
        EmitDictionaryApply(
            code,
            member,
            keyType,
            valueType,
            dictType,
            runtime,
            kind,
            optionalDict,
            comparer,
            hasPatch
        );
        EmitDictionaryBetween(
            code,
            member,
            patchName,
            keyType,
            valueType,
            runtime,
            operation,
            optionalDict,
            facade,
            comparer,
            hasPatch
        );
        SparseDictionaryAlgebraEmitter.EmitDictionaryCompose(
            code,
            member,
            patchName,
            keyType,
            valueType,
            dictType,
            runtime,
            operation,
            kind,
            comparer,
            hasPatch
        );
        code.AppendLineAt(
            3,
            "public "
                + patchName
                + " Invert("
                + optionalDict
                + " baseline) => Between(Apply(baseline), baseline);"
        );
        SparseDictionaryAlgebraEmitter.EmitDictionaryRebase(
            code,
            member,
            patchName,
            keyType,
            valueType,
            runtime,
            kind,
            optionalDict,
            facade,
            comparer,
            hasPatch,
            editedValueType
        );
        SparsePatchStjEmitter.AppendDictionaryStj(code, member);
        code.AppendLineAt(2, "}");
    }

    internal static void EmitDictionarySurface(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string dictType,
        string runtime,
        string operation,
        string kind,
        string comparer,
        bool hasPatch,
        string editedValueType
    )
    {
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
        if (!SparseKeyedCollectionEmitter.IsInterfaceMember(member))
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
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
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
                    + SparseKeyedCollectionEmitter.ValuePatchType(member)
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
                    + SparseKeyedCollectionEmitter.ValuePatchType(member)
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(4, "__edited[key] = patch;");
            code.AppendLineAt(3, "}");
        }
    }

    internal static void EmitDictionaryApply(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType,
        string valueType,
        string dictType,
        string runtime,
        string kind,
        string optionalDict,
        string comparer,
        bool hasPatch
    )
    {
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
            var valueFragment = SparseKeyedCollectionEmitter.ValueFragmentType(member);
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
                + SparseKeyedCollectionEmitter.ConvertDictionaryToMember(member, "result")
                + ");"
        );
        code.AppendLineAt(3, "}");
    }

    internal static void EmitDictionaryBetween(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string runtime,
        string operation,
        string optionalDict,
        string facade,
        string comparer,
        bool hasPatch
    )
    {
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
            var valueFragment = SparseKeyedCollectionEmitter.ValueFragmentType(member);
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            var prefix = SparseKeyedCollectionEmitter.ValuePatchPrefix(member);
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
    }
}
