namespace SparseFragments.Generator.Shared;

/// <summary>Emits the keyed-sequence patch surface: declaration plus Add/Remove/Edit/Update/SetOrder mutators.</summary>
internal static class SparseKeyedSequenceSurfaceEmitter
{
    internal static void EmitKeyedSequencePatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType = null,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        var elementType = SparseKeyedCollectionEmitter.ElementType(member);
        var keyType = SparseKeyedCollectionEmitter.KeyType(member);
        var listType = SparseKeyedCollectionEmitter.MemberListType(member);
        var runtime = dialect.RuntimeNamespace;
        var operation = runtime + "FragmentOperation<" + listType + ">";
        var kind = runtime + "FragmentOperationKind";
        var optionalList = runtime + "Optional<" + listType + ">";
        var facade = dialect.RuntimeFacade;
        var hasPatch = SparseKeyedCollectionEmitter.HasElementPatch(member);
        var editedValueType = hasPatch
            ? SparseKeyedCollectionEmitter.ElementPatchType(member)
            : elementType;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        // Shared removal prefix (#183); null keeps the legacy private kernels.
        var sharedRemoval = string.IsNullOrEmpty(implementationNamespace)
            ? null
            : SparseDictionaryRemovalIndexEmitter.SharedPrefix(keyType, implementationNamespace!);

        code.AppendLineAt(
            2,
            "/// <summary>Keyed collection patch for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "public sealed class " + patchName);
        code.AppendLineAt(2, "{");
        // Public-first order for relocated patches: internal backing fields
        // and helpers trail public mutators and algebra stubs.
        var surfaceDeferred = target is null
            ? null
            : new SharedIndentedBuilder(code.CancellationToken);
        var internalCode = surfaceDeferred ?? code;
        // Relocated operation bodies reach state through these fields, so
        // they are internal rather than private. The mutating surface below
        // stays on the facade as thin delegating stubs.
        internalCode.AppendLineAt(3, "internal " + operation + " __whole;");
        internalCode.AppendLineAt(
            3,
            "internal global::System.Collections.Generic.List<" + elementType + ">? __added;"
        );
        internalCode.AppendLineAt(
            3,
            "internal global::System.Collections.Generic.List<" + keyType + ">? __removed;"
        );
        internalCode.AppendLineAt(
            3,
            "internal global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedValueType
                + ">? __edited;"
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseKeyedSequenceTempSurfaceEmitter.AppendTempFields(internalCode, editedValueType);
        }
        internalCode.AppendLineAt(
            3,
            "internal global::System.Collections.Generic.List<" + keyType + ">? __order;"
        );
        SparseMemberOperationSplit? split = null;
        SharedIndentedBuilder operations = code;
        SharedIndentedBuilder? deferredKeyOf = null;
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
            // Private key helper trails the internal operation bodies so the
            // member operation class stays public/internal-first ordered.
            deferredKeyOf = new SharedIndentedBuilder(code.CancellationToken)
            {
                IndentOffset = operations.IndentOffset,
            };
            SparseKeyedCollectionEmitter.EmitKeyOf(deferredKeyOf, member, elementType, 3);
        }
        else
        {
            SparseKeyedCollectionEmitter.EmitKeyOf(code, member, elementType, 3);
        }
        code.AppendLineAt(3, "/// <summary>Whether this keyed patch carries no changes.</summary>");
        code.AppendLineAt(3, "public bool IsEmpty => __whole.Kind == " + kind + ".Keep");
        code.AppendLineAt(
            4,
            "&& (__added is null || __added.Count == 0) && (__removed is null || __removed.Count == 0)"
        );
        code.AppendLineAt(
            4,
            "&& (__edited is null || __edited.Count == 0) && (__order is null || __order.Count == 0)"
                + (
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? SparseKeyedSequenceTempSurfaceEmitter.TempIsEmptyFragment(string.Empty)
                        : ""
                )
                + ";"
        );
        internalCode.AppendLineAt(3, "internal bool __SparseIsEmpty() => IsEmpty;");
        // Whole operations (public group).
        code.AppendLineAt(3, "/// <summary>Replaces the whole collection.</summary>");
        code.AppendLineAt(3, "/// <param name=\"value\">Replacement values.</param>");
        code.AppendLineAt(3, "public void Set(" + listType + " value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__whole = " + operation + ".Set(value);");
        code.AppendLineAt(4, "__added = null; __removed = null; __edited = null; __order = null;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "/// <summary>Removes the whole collection.</summary>");
        code.AppendLineAt(3, "public void Remove()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__whole = " + operation + ".Remove;");
        code.AppendLineAt(4, "__added = null; __removed = null; __edited = null; __order = null;");
        code.AppendLineAt(3, "}");
        if (!SparseKeyedCollectionEmitter.IsInterfaceMember(member))
        {
            code.AppendLineAt(3, "/// <summary>Creates a keyed patch from values.</summary>");
            code.AppendLineAt(3, "/// <param name=\"value\">Replacement values.</param>");
            code.AppendLineAt(3, "/// <returns>A patch carrying the values.</returns>");
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
        if (surfaceDeferred is not null)
        {
            SparseKeyedRemovalIndexEmitter.Emit(
                surfaceDeferred,
                keyType,
                comparer,
                implementationNamespace
            );
        }
        else
        {
            SparseKeyedRemovalIndexEmitter.Emit(code, keyType, comparer, implementationNamespace);
        }
        // Relocated mutator bodies reuse this guard through the facade instance.
        internalCode.AppendLineAt(3, "internal void EnsureGranular(string operation)");
        internalCode.AppendLineAt(3, "{");
        internalCode.AppendLineAt(
            4,
            "if (__whole.Kind != "
                + kind
                + ".Keep) throw new global::System.InvalidOperationException(\"Cannot apply '\" + operation + \"' when the whole collection is set. Clear the whole operation first.\");"
        );
        internalCode.AppendLineAt(3, "}");
        EmitKeyedMutators(
            operations,
            member,
            patchName,
            elementType,
            keyType,
            editedValueType,
            comparer,
            facade,
            sharedRemoval,
            hasPatch,
            implementationNamespace,
            split
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseKeyedSequenceTempSurfaceEmitter.AppendTempMutators(
                code,
                member,
                patchName,
                elementType,
                hasPatch,
                split
            );
        }
        if (hasPatch)
        {
            internalCode.AppendLineAt(
                3,
                "internal void __SparseSetEdited("
                    + keyType
                    + " key, "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + " patch)"
            );
            internalCode.AppendLineAt(3, "{");
            internalCode.AppendLineAt(4, "EnsureGranular(\"SetEdited\");");
            internalCode.AppendLineAt(
                4,
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            internalCode.AppendLineAt(
                4,
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + ");"
            );
            internalCode.AppendLineAt(4, "__edited[key] = patch;");
            internalCode.AppendLineAt(3, "}");
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                internalCode.AppendLineAt(
                    3,
                    "internal void __SparseSetEditedByTemporaryKey(global::System.Guid temporaryKey, "
                        + SparseKeyedCollectionEmitter.ElementPatchType(member)
                        + " patch)"
                );
                internalCode.AppendLineAt(3, "{");
                internalCode.AppendLineAt(4, "EnsureGranular(\"SetEditedByTemporaryKey\");");
                internalCode.AppendLineAt(
                    4,
                    "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
                );
                internalCode.AppendLineAt(
                    4,
                    "if (temporaryKey == global::System.Guid.Empty) throw new global::System.ArgumentException(\"Temporary identity must not be empty.\", nameof(temporaryKey));"
                );
                internalCode.AppendLineAt(
                    4,
                    "__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                        + SparseKeyedCollectionEmitter.ElementPatchType(member)
                        + ">();"
                );
                internalCode.AppendLineAt(4, "__editedTemp[temporaryKey] = patch;");
                internalCode.AppendLineAt(3, "}");
            }
        }
        SparseKeyedSequenceApplyEmitter.EmitKeyedApply(
            operations,
            member,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            runtime,
            split
        );
        SparseKeyedSequenceApplyEmitter.EmitKeyedBetween(
            operations,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            facade,
            runtime,
            split
        );
        SparseKeyedSequenceComposeEmitter.EmitKeyedCompose(
            operations,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            runtime,
            split
        );
        if (split is not null)
        {
            code.AppendLineAt(
                3,
                "/// <summary>Inverts this collection patch relative to the baseline it was applied to.</summary>"
            );
            code.AppendLineAt(3, "/// <param name=\"baseline\">Baseline applied to.</param>");
            code.AppendLineAt(3, "/// <returns>A patch undoing this patch.</returns>");
            code.AppendLineAt(
                3,
                "public "
                    + patchName
                    + " Invert("
                    + optionalList
                    + " baseline) => "
                    + split.OperationsType
                    + ".Invert(this, baseline);"
            );
            operations.AppendLineAt(
                3,
                "/// <summary>Inverts a collection patch relative to the baseline it was applied to.</summary>"
            );
            operations.AppendLineAt(
                3,
                "internal static "
                    + patchName
                    + " Invert("
                    + patchName
                    + " self, "
                    + optionalList
                    + " baseline) => Between(Apply(self, baseline), baseline);"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "/// <summary>Inverts this patch relative to its baseline.</summary>"
            );
            code.AppendLineAt(3, "/// <param name=\"baseline\">Baseline applied to.</param>");
            code.AppendLineAt(3, "/// <returns>A patch undoing this patch.</returns>");
            code.AppendLineAt(
                3,
                "public "
                    + patchName
                    + " Invert("
                    + optionalList
                    + " baseline) => Between(Apply(baseline), baseline);"
            );
        }
        SparseKeyedSequenceRebaseEmitter.EmitKeyedRebase(
            operations,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            facade,
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
        if (surfaceDeferred is not null)
        {
            code.Append(surfaceDeferred.ToString());
        }
        if (target is not null)
        {
            if (deferredKeyOf is not null)
            {
                operations.Append(deferredKeyOf.ToString());
            }

            target.CloseMemberOperations(
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations"
            );
        }
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits the keyed mutators (Add/Remove/Edit/Update/SetOrder).</summary>
    /// <remarks>
    /// With a null split the mutators stay instance members of the nested
    /// patch (legacy single-file emission). Otherwise thin stubs stay on the
    /// facade while the bodies move into the member operation class,
    /// aliasing facade fields by reference so element identity and
    /// in-place mutation semantics are unchanged.
    /// </remarks>
    internal static void EmitKeyedMutators(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string editedValueType,
        string comparer,
        string facade,
        string? sharedRemoval,
        bool hasPatch,
        string? implementationNamespace,
        SparseMemberOperationSplit? split
    )
    {
        var relocated = split is not null;
        var guard = relocated ? "self." : string.Empty;
        // Add.
        if (relocated)
        {
            split!.Shell.AppendLineAt(
                3,
                "/// <summary>Adds an element to this keyed patch.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public void Add("
                    + elementType
                    + " element) => "
                    + split.OperationsType
                    + ".Add(this, element);"
            );
            code.AppendLineAt(3, "/// <summary>Adds an element to a keyed patch.</summary>");
            code.AppendLineAt(
                3,
                "internal static void Add(" + patchName + " self, " + elementType + " element)"
            );
            code.AppendLineAt(3, "{");
            AppendFieldAliases(code, member);
        }
        else
        {
            code.AppendLineAt(3, "/// <summary>Adds an element to this keyed patch.</summary>");
            code.AppendLineAt(3, "public void Add(" + elementType + " element)");
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(
            4,
            "if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));"
        );
        code.AppendLineAt(4, guard + "EnsureGranular(\"Add\");");
        code.AppendLineAt(
            4,
            "var key = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(element);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                "if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + " && __added is not null) foreach (var existing in __added) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(existing), key)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection patch.\");"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "if (__added is not null) foreach (var existing in __added) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(existing), key)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection patch.\");"
            );
        }
        code.AppendLineAt(
            4,
            "if (__edited is not null"
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? " && !"
                            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        : ""
                )
                + " && __edited.ContainsKey(key!)) throw new global::System.InvalidOperationException(\"Key is already edited in this patch.\");"
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary adds stay separately addressable: duplicate
            // identities fail at record time instead of colliding at apply.
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + ") { var __addTemp = "
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(element); if (__addTemp.HasValue && __addTemp.Value != global::System.Guid.Empty && __added is not null) foreach (var existing in __added) if ("
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(existing) == __addTemp.Value) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection patch.\"); }"
            );
        }
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                sharedRemoval is null
                    ? "if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + " && __removed is not null) __SparseCancelRemoval(key);"
                    : "if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + " && __removed is not null) "
                        + sharedRemoval
                        + ".CancelRemoval(__removed!, ref __removedLookup, key);"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                sharedRemoval is null
                    ? "if (__removed is not null) __SparseCancelRemoval(key);"
                    : "if (__removed is not null) "
                        + sharedRemoval
                        + ".CancelRemoval(__removed!, ref __removedLookup, key);"
            );
        }
        code.AppendLineAt(
            4,
            "__added ??= new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(4, "__added.Add(element);");
        code.AppendLineAt(3, "}");
        // Remove.
        if (relocated)
        {
            split!.Shell.AppendLineAt(
                3,
                "/// <summary>Removes an element from this keyed patch by key.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public void Remove("
                    + keyType
                    + " key) => "
                    + split.OperationsType
                    + ".Remove(this, key);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Removes an element from a keyed patch by key.</summary>"
            );
            code.AppendLineAt(
                3,
                "internal static void Remove(" + patchName + " self, " + keyType + " key)"
            );
            code.AppendLineAt(3, "{");
            AppendFieldAliases(code, member);
        }
        else
        {
            code.AppendLineAt(3, "/// <summary>Removes an element by key.</summary>");
            code.AppendLineAt(3, "public void Remove(" + keyType + " key)");
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(4, guard + "EnsureGranular(\"Remove\");");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be removed by key.\");"
            );
        code.AppendLineAt(
            4,
            "if (__added is not null) { for (var i = __added.Count - 1; i >= 0; i--) if ("
                + comparer
                + ".Equals("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__added[i]), key)) { __added.RemoveAt(i); return; } }"
        );
        code.AppendLineAt(4, "if (__edited is not null) __edited.Remove(key);");
        code.AppendLineAt(
            4,
            "__removed ??= new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        SparseDictionaryRemovalIndexEmitter.EmitAdd(
            code,
            comparer,
            keepReservedIndex: false,
            keyType,
            implementationNamespace
        );
        code.AppendLineAt(3, "}");
        // Edit / Update.
        if (hasPatch)
        {
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            if (relocated)
            {
                split!.Shell.AppendLineAt(
                    3,
                    "/// <summary>Gets the edit patch for an element of this keyed patch.</summary>"
                );
                split.Shell.AppendLineAt(
                    3,
                    "public "
                        + elementPatch
                        + " Edit("
                        + keyType
                        + " key) => "
                        + split.OperationsType
                        + ".Edit(this, key);"
                );
                code.AppendLineAt(
                    3,
                    "/// <summary>Gets the edit patch for an element of a keyed patch.</summary>"
                );
                code.AppendLineAt(
                    3,
                    "internal static "
                        + elementPatch
                        + " Edit("
                        + patchName
                        + " self, "
                        + keyType
                        + " key)"
                );
                code.AppendLineAt(3, "{");
                AppendFieldAliases(code, member);
            }
            else
            {
                code.AppendLineAt(3, "/// <summary>Gets the edit patch for an element.</summary>");
                code.AppendLineAt(3, "/// <param name=\"key\">Key to edit.</param>");
                code.AppendLineAt(3, "/// <returns>The edit patch.</returns>");
                code.AppendLineAt(3, "public " + elementPatch + " Edit(" + keyType + " key)");
                code.AppendLineAt(3, "{");
            }
            code.AppendLineAt(4, guard + "EnsureGranular(\"Edit\");");
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
                code.AppendLineAt(
                    4,
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be edited by key.\");"
                );
            code.AppendLineAt(
                4,
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed element. Add it again instead.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed element. Add it again instead.\");"
            );
            code.AppendLineAt(
                4,
                "if (__added is not null) foreach (var a in __added) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
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
            if (relocated)
            {
                split!.Shell.AppendLineAt(
                    3,
                    "/// <summary>Updates an element of this keyed patch in place.</summary>"
                );
                split.Shell.AppendLineAt(
                    3,
                    "public void Update("
                        + elementType
                        + " element) => "
                        + split.OperationsType
                        + ".Update(this, element);"
                );
                code.AppendLineAt(
                    3,
                    "/// <summary>Updates an element of a keyed patch in place.</summary>"
                );
                code.AppendLineAt(
                    3,
                    "internal static void Update("
                        + patchName
                        + " self, "
                        + elementType
                        + " element)"
                );
                code.AppendLineAt(3, "{");
                AppendFieldAliases(code, member);
            }
            else
            {
                code.AppendLineAt(3, "/// <summary>Updates an element in place.</summary>");
                code.AppendLineAt(3, "public void Update(" + elementType + " element)");
                code.AppendLineAt(3, "{");
            }
            code.AppendLineAt(
                4,
                "if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));"
            );
            code.AppendLineAt(4, guard + "EnsureGranular(\"Update\");");
            code.AppendLineAt(
                4,
                "var key = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(element);"
            );
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                // Unassigned elements with an identity update by Guid; the
                // dedicated mutator owns that path so identity never collides.
                var updateByTemp = split is not null
                    ? split.OperationsType + ".UpdateByTemporaryKey(self, element)"
                    : "UpdateByTemporaryKey(element)";
                code.AppendLineAt(
                    4,
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + " && "
                        + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                        + "(element).HasValue) { "
                        + updateByTemp
                        + "; return; }"
                );
            }
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
                code.AppendLineAt(
                    4,
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be updated by key; add it as a new item instead.\");"
                );
            code.AppendLineAt(
                4,
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
            );
            code.AppendLineAt(
                4,
                "if (__added is not null) for (var i = 0; i < __added.Count; i++) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
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

        if (relocated)
        {
            split!.Shell.AppendLineAt(
                3,
                "/// <summary>Replaces the key order of this keyed patch.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public void SetOrder(global::System.Collections.Generic.IEnumerable<"
                    + keyType
                    + "> keys) => "
                    + split.OperationsType
                    + ".SetOrder(this, keys);"
            );
            code.AppendLineAt(3, "/// <summary>Replaces the key order of a keyed patch.</summary>");
            code.AppendLineAt(
                3,
                "internal static void SetOrder("
                    + patchName
                    + " self, global::System.Collections.Generic.IEnumerable<"
                    + keyType
                    + "> keys)"
            );
            code.AppendLineAt(3, "{");
            AppendFieldAliases(code, member);
        }
        else
        {
            code.AppendLineAt(3, "/// <summary>Replaces the key order.</summary>");
            code.AppendLineAt(3, "/// <param name=\"keys\">Replacement order.</param>");
            code.AppendLineAt(
                3,
                "public void SetOrder(global::System.Collections.Generic.IEnumerable<"
                    + keyType
                    + "> keys)"
            );
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(
            4,
            "if (keys is null) throw new global::System.ArgumentNullException(nameof(keys));"
        );
        code.AppendLineAt(4, guard + "EnsureGranular(\"SetOrder\");");
        code.AppendLineAt(
            4,
            "var list = new global::System.Collections.Generic.List<" + keyType + ">(keys);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                "var __seenOrder = new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">("
                    + comparer
                    + "); foreach (var __key in list) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__key")
                    + " && !__seenOrder.Add(__key)) throw new global::System.InvalidOperationException(\"Duplicate order key.\");"
            );
        }
        else
            code.AppendLineAt(4, facade + ".EnsureUniqueKeys<" + keyType + ">(list);");
        code.AppendLineAt(4, "__order = list;");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Explicit key orders carry no temporary fidelity; later applies
            // fall back to positional legacy flow for bare sentinel slots.
            code.AppendLineAt(4, "__orderTemp = null;");
        }
        code.AppendLineAt(3, "}");
    }

    /// <summary>Aliases nested keyed-patch fields by reference for relocated bodies.</summary>
    /// <remarks>
    /// Relocated mutator and algebra bodies operate on the facade instance
    /// through these aliases, so element identity, lazy nested state and
    /// in-place mutation semantics match the legacy instance methods exactly.
    /// </remarks>
    internal static void AppendFieldAliases(
        SharedIndentedBuilder code,
        SparseMemberModel? member = null
    )
    {
        code.AppendLineAt(4, "ref var __whole = ref self.__whole;");
        code.AppendLineAt(4, "ref var __added = ref self.__added;");
        code.AppendLineAt(4, "ref var __removed = ref self.__removed;");
        code.AppendLineAt(4, "ref var __removedLookup = ref self.__removedLookup;");
        code.AppendLineAt(4, "ref var __edited = ref self.__edited;");
        code.AppendLineAt(4, "ref var __order = ref self.__order;");
        if (member.HasValue && SparseKeyedCollectionEmitter.HasTemporaryKey(member.Value))
        {
            SparseKeyedSequenceTempSurfaceEmitter.AppendTempFieldAliases(code);
        }
    }
}
