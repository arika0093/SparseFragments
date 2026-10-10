namespace SparseFragments.Generator.Shared;

/// <summary>Emits the dictionary patch surface plus Apply and Between algebra.</summary>
internal static class SparseDictionaryPatchEmitter
{
    internal static void EmitDictionaryPatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType = null,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        var keyType = SparseKeyedCollectionEmitter.KeyType(member);
        var valueType = SparseKeyedCollectionEmitter.DictionaryValueType(member);
        var dictType = SparseKeyedCollectionEmitter.DictionaryType(member);
        var runtime = dialect.RuntimeNamespace;
        var operation = runtime + "FragmentOperation<" + dictType + ">";
        var kind = runtime + "FragmentOperationKind";
        var optionalDict = runtime + "Optional<" + dictType + ">";
        var facade = dialect.RuntimeFacade;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var hasPatch = SparseKeyedCollectionEmitter.HasValuePatch(member);
        var editedValueType = hasPatch
            ? SparseKeyedCollectionEmitter.ValuePatchType(member)
            : valueType;

        SparseMemberOperationSplit? split = null;
        SharedIndentedBuilder operations = code;
        if (target is not null)
        {
            var operationsType = SparseOperationTarget.MemberOperationsType(
                target.PatchOperationsType,
                member
            );
            operations = target.OpenMemberOperations(
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations",
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations"
            );
            split = new SparseMemberOperationSplit(code, operations, operationsType, "self.");
        }
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
            editedValueType,
            implementationNamespace,
            split
        );
        SparseDictionaryRemovalIndexEmitter.Emit(code, keyType, comparer, implementationNamespace);
        EmitDictionaryApply(
            operations,
            member,
            keyType,
            valueType,
            dictType,
            runtime,
            kind,
            optionalDict,
            comparer,
            hasPatch,
            split
        );
        EmitDictionaryBetween(
            operations,
            member,
            patchName,
            keyType,
            valueType,
            runtime,
            operation,
            optionalDict,
            facade,
            comparer,
            hasPatch,
            split
        );
        SparseDictionaryAlgebraEmitter.EmitDictionaryCompose(
            operations,
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
            split
        );
        if (split is not null)
        {
            code.AppendLineAt(
                3,
                "/// <summary>Inverts this dictionary patch relative to the baseline it was applied to.</summary>"
            );
            code.AppendLineAt(
                3,
                "public "
                    + patchName
                    + " Invert("
                    + optionalDict
                    + " baseline) => "
                    + split.OperationsType
                    + ".Invert(this, baseline);"
            );
            operations.AppendLineAt(
                3,
                "/// <summary>Inverts a dictionary patch relative to the baseline it was applied to.</summary>"
            );
            operations.AppendLineAt(
                3,
                "internal static "
                    + patchName
                    + " Invert("
                    + patchName
                    + " self, "
                    + optionalDict
                    + " baseline) => Between(Apply(self, baseline), baseline);"
            );
        }
        else
        {
            code.AppendLineAt(3, "/// <summary>Inverts this dictionary patch.</summary>");
            code.AppendLineAt(3, "/// <param name=\"baseline\">Baseline applied to.</param>");
            code.AppendLineAt(3, "/// <returns>A patch undoing this patch.</returns>");
            code.AppendLineAt(
                3,
                "public "
                    + patchName
                    + " Invert("
                    + optionalDict
                    + " baseline) => Between(Apply(baseline), baseline);"
            );
        }
        SparseDictionaryAlgebraEmitter.EmitDictionaryRebase(
            operations,
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
            editedValueType,
            dialect,
            split
        );
        if (modelType is not null)
            SparseChangePayloadCollectionExportEmitter.AppendCollectionExport(
                operations,
                member,
                dialect,
                modelType,
                implementationNamespace,
                split
            );
        if (target is not null)
        {
            target.CloseMemberOperations(
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations"
            );
        }
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
        string editedValueType,
        string? implementationNamespace = null,
        SparseMemberOperationSplit? split = null
    )
    {
        // Shared removal prefix (#183); null keeps the legacy private kernels.
        var sharedRemoval = string.IsNullOrEmpty(implementationNamespace)
            ? null
            : SparseDictionaryRemovalIndexEmitter.SharedPrefix(keyType, implementationNamespace!);
        code.AppendLineAt(
            2,
            "/// <summary>Dictionary patch for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLineAt(2, "public sealed class " + patchName);
        code.AppendLineAt(2, "{");
        // Relocated operation bodies reach state through these fields.
        code.AppendLineAt(3, "internal " + operation + " __whole;");
        code.AppendLineAt(
            3,
            "internal global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">? __set;"
        );
        code.AppendLineAt(
            3,
            "internal global::System.Collections.Generic.List<" + keyType + ">? __removed;"
        );
        code.AppendLineAt(3, "internal int[]? __removedLookup;");
        code.AppendLineAt(
            3,
            hasPatch
                ? "internal global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + editedValueType
                    + ">? __edited;"
                : "internal global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">? __edited;"
        );
        code.AppendLineAt(
            3,
            "/// <summary>Whether this dictionary patch carries no changes.</summary>"
        );
        code.AppendLineAt(3, "public bool IsEmpty => __whole.Kind == " + kind + ".Keep");
        code.AppendLineAt(
            4,
            "&& (__set is null || __set.Count == 0) && (__removed is null || __removed.Count == 0) && (__edited is null || __edited.Count == 0);"
        );
        code.AppendLineAt(3, "internal bool __SparseIsEmpty() => IsEmpty;");
        code.AppendLineAt(3, "/// <summary>Replaces the whole dictionary.</summary>");
        code.AppendLineAt(3, "/// <param name=\"value\">Replacement values.</param>");
        code.AppendLineAt(3, "public void Set(" + dictType + " value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "__whole = "
                + operation
                + ".Set(value); __set = null; __removed = null; __removedLookup = null; __edited = null;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "/// <summary>Removes the whole dictionary.</summary>");
        code.AppendLineAt(3, "public void Remove()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "__whole = "
                + operation
                + ".Remove; __set = null; __removed = null; __removedLookup = null; __edited = null;"
        );
        code.AppendLineAt(3, "}");
        if (!SparseKeyedCollectionEmitter.IsInterfaceMember(member))
        {
            code.AppendLineAt(3, "/// <summary>Creates a dictionary patch from values.</summary>");
            code.AppendLineAt(3, "/// <param name=\"value\">Replacement values.</param>");
            code.AppendLineAt(3, "/// <returns>A patch carrying the values.</returns>");
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
        code.AppendLineAt(3, "internal void EnsureGranular(string operation)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__whole.Kind != "
                + kind
                + ".Keep) throw new global::System.InvalidOperationException(\"Cannot apply '\" + operation + \"' when the whole dictionary is set.\");"
        );
        code.AppendLineAt(3, "}");
        EmitDictionaryMutators(
            split?.Operations ?? code,
            member,
            patchName,
            keyType,
            valueType,
            comparer,
            sharedRemoval,
            hasPatch,
            implementationNamespace,
            split
        );
        // Internal sparse setter for ChangeSet.ToPatch; installs an already-built value patch.
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
        bool hasPatch,
        SparseMemberOperationSplit? split = null
    )
    {
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        if (split is not null)
        {
            split.Shell.AppendLineAt(
                3,
                "/// <summary>Applies this dictionary patch to a member value.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public "
                    + optionalDict
                    + " Apply("
                    + optionalDict
                    + " current) => "
                    + split.OperationsType
                    + ".Apply(this, current);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Applies a dictionary patch to a member value.</summary>"
            );
            code.AppendLineAt(
                3,
                "internal static "
                    + optionalDict
                    + " Apply("
                    + patchName
                    + " self, "
                    + optionalDict
                    + " current)"
            );
            code.AppendLineAt(3, "{");
            AppendDictionaryFieldAliases(code);
        }
        else
        {
            // Apply.
            code.AppendLineAt(3, "/// <summary>Applies this dictionary patch.</summary>");
            code.AppendLineAt(3, "/// <param name=\"current\">Value to apply to.</param>");
            code.AppendLineAt(3, "/// <returns>The value with the patch applied.</returns>");
            code.AppendLineAt(3, "public " + optionalDict + " Apply(" + optionalDict + " current)");
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(
            4,
            "if (__whole.Kind != " + kind + ".Keep) return __whole.Apply(current);"
        );
        code.AppendLineAt(
            4,
            "if (" + (split is null ? string.Empty : "self.") + "IsEmpty) return current;"
        );
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
        bool hasPatch,
        SparseMemberOperationSplit? split = null
    )
    {
        if (split is not null)
        {
            split.Shell.AppendLineAt(
                3,
                "/// <summary>Derives a dictionary patch between two member values.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public static "
                    + patchName
                    + " Between("
                    + optionalDict
                    + " before, "
                    + optionalDict
                    + " after) => "
                    + split.OperationsType
                    + ".Between(before, after);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Derives a dictionary patch between two member values.</summary>"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "/// <summary>Derives a dictionary patch between values.</summary>"
            );
            code.AppendLineAt(3, "/// <param name=\"before\">Value before.</param>");
            code.AppendLineAt(3, "/// <param name=\"after\">Value after.</param>");
            code.AppendLineAt(3, "/// <returns>A patch turning before into after.</returns>");
        }
        // Between.
        code.AppendLineAt(
            3,
            (split is null ? "public static " : "internal static ")
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
                + ".Remove;"
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
