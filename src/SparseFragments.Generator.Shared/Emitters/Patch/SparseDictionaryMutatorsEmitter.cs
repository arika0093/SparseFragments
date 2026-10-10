namespace SparseFragments.Generator.Shared;

/// <summary>Emits dictionary patch mutators.</summary>
/// <remarks>Extracted from <c>SparseDictionaryPatchEmitter</c> to keep that file under the size guideline; it owns only the SetEntry/RemoveEntry/Edit/UpdateEntry surface split with no semantic changes.</remarks>
internal static class SparseDictionaryMutatorsEmitter
{
    /// <summary>Emits the dictionary mutators (SetEntry/RemoveEntry/Edit/UpdateEntry).</summary>
    /// <remarks>
    /// With a null split the mutators stay instance members of the nested
    /// patch (legacy single-file emission). Otherwise thin stubs stay on the
    /// facade while the bodies move into the member operation class,
    /// aliasing facade fields by reference so mutation semantics match.
    /// </remarks>
    internal static void EmitDictionaryMutators(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string comparer,
        string? sharedRemoval,
        bool hasPatch,
        string? implementationNamespace,
        SparseMemberOperationSplit? split
    )
    {
        var relocated = split is not null;
        var guard = relocated ? "self." : string.Empty;
        if (relocated)
        {
            split!.Shell.AppendLineAt(
                3,
                "/// <summary>Sets an entry of this dictionary patch.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public void SetEntry("
                    + keyType
                    + " key, "
                    + valueType
                    + " value) => "
                    + split.OperationsType
                    + ".SetEntry(this, key, value);"
            );
            code.AppendLineAt(3, "/// <summary>Sets an entry of a dictionary patch.</summary>");
            code.AppendLineAt(
                3,
                "internal static void SetEntry("
                    + patchName
                    + " self, "
                    + keyType
                    + " key, "
                    + valueType
                    + " value)"
            );
            code.AppendLineAt(3, "{");
            AppendDictionaryFieldAliases(code);
        }
        else
        {
            code.AppendLineAt(3, "/// <summary>Sets an entry of this dictionary patch.</summary>");
            code.AppendLineAt(
                3,
                "public void SetEntry(" + keyType + " key, " + valueType + " value)"
            );
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(4, guard + "EnsureGranular(\"SetEntry\");");
        code.AppendLineAt(
            4,
            sharedRemoval is null
                ? "if (__removed is not null) __SparseCancelRemoval(key);"
                : "if (__removed is not null) "
                    + sharedRemoval
                    + ".CancelRemoval(__removed!, ref __removedLookup, key);"
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
        if (relocated)
        {
            split!.Shell.AppendLineAt(
                3,
                "/// <summary>Removes an entry of this dictionary patch by key.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public void RemoveEntry("
                    + keyType
                    + " key) => "
                    + split.OperationsType
                    + ".RemoveEntry(this, key);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Removes an entry of a dictionary patch by key.</summary>"
            );
            code.AppendLineAt(
                3,
                "internal static void RemoveEntry(" + patchName + " self, " + keyType + " key)"
            );
            code.AppendLineAt(3, "{");
            AppendDictionaryFieldAliases(code);
        }
        else
        {
            code.AppendLineAt(3, "/// <summary>Removes an entry by key.</summary>");
            code.AppendLineAt(3, "public void RemoveEntry(" + keyType + " key)");
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(4, guard + "EnsureGranular(\"RemoveEntry\");");
        code.AppendLineAt(4, "if (__set is not null) { if (__set.Remove(key)) return; }");
        code.AppendLineAt(4, "if (__edited is not null) __edited.Remove(key);");
        code.AppendLineAt(
            4,
            "__removed ??= new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        SparseDictionaryRemovalIndexEmitter.EmitAdd(
            code,
            comparer,
            keepReservedIndex: true,
            keyType,
            implementationNamespace
        );
        code.AppendLineAt(3, "}");
        if (hasPatch)
        {
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            if (relocated)
            {
                split!.Shell.AppendLineAt(
                    3,
                    "/// <summary>Gets the edit patch for an entry of this dictionary patch.</summary>"
                );
                split.Shell.AppendLineAt(
                    3,
                    "public "
                        + valuePatch
                        + " Edit("
                        + keyType
                        + " key) => "
                        + split.OperationsType
                        + ".Edit(this, key);"
                );
                code.AppendLineAt(
                    3,
                    "/// <summary>Gets the edit patch for an entry of a dictionary patch.</summary>"
                );
                code.AppendLineAt(
                    3,
                    "internal static "
                        + valuePatch
                        + " Edit("
                        + patchName
                        + " self, "
                        + keyType
                        + " key)"
                );
                code.AppendLineAt(3, "{");
                AppendDictionaryFieldAliases(code);
            }
            else
            {
                code.AppendLineAt(3, "/// <summary>Gets the edit patch for an entry.</summary>");
                code.AppendLineAt(3, "public " + valuePatch + " Edit(" + keyType + " key)");
                code.AppendLineAt(3, "{");
            }
            code.AppendLineAt(4, guard + "EnsureGranular(\"Edit\");");
            code.AppendLineAt(
                4,
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed entry.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed entry.\");"
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
            if (relocated)
            {
                split!.Shell.AppendLineAt(
                    3,
                    "/// <summary>Updates an entry of this dictionary patch.</summary>"
                );
                split.Shell.AppendLineAt(
                    3,
                    "public void UpdateEntry("
                        + keyType
                        + " key, "
                        + valueType
                        + " value) => "
                        + split.OperationsType
                        + ".UpdateEntry(this, key, value);"
                );
                code.AppendLineAt(
                    3,
                    "/// <summary>Updates an entry of a dictionary patch.</summary>"
                );
                code.AppendLineAt(
                    3,
                    "internal static void UpdateEntry("
                        + patchName
                        + " self, "
                        + keyType
                        + " key, "
                        + valueType
                        + " value)"
                );
                code.AppendLineAt(3, "{");
                AppendDictionaryFieldAliases(code);
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "/// <summary>Updates an entry of this dictionary patch.</summary>"
                );
                code.AppendLineAt(
                    3,
                    "public void UpdateEntry(" + keyType + " key, " + valueType + " value)"
                );
                code.AppendLineAt(3, "{");
            }
            code.AppendLineAt(4, guard + "EnsureGranular(\"UpdateEntry\");");
            code.AppendLineAt(
                4,
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot update a removed entry. Set it again instead.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot update a removed entry. Set it again instead.\");"
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
    }

    /// <summary>Aliases nested dictionary-patch fields by reference for relocated bodies.</summary>
    internal static void AppendDictionaryFieldAliases(SharedIndentedBuilder code)
    {
        code.AppendLineAt(4, "ref var __whole = ref self.__whole;");
        code.AppendLineAt(4, "ref var __set = ref self.__set;");
        code.AppendLineAt(4, "ref var __removed = ref self.__removed;");
        code.AppendLineAt(4, "ref var __removedLookup = ref self.__removedLookup;");
        code.AppendLineAt(4, "ref var __edited = ref self.__edited;");
    }
}
