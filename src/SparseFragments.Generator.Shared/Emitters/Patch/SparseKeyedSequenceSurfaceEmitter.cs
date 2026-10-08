namespace SparseFragments.Generator.Shared;

/// <summary>Emits the keyed-sequence patch surface: declaration plus Add/Remove/Edit/Update/SetOrder mutators.</summary>
internal static class SparseKeyedSequenceSurfaceEmitter
{
    internal static void EmitKeyedSequencePatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
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
        SparseKeyedCollectionEmitter.EmitKeyOf(code, member, elementType, 3);
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
        if (!SparseKeyedCollectionEmitter.IsInterfaceMember(member))
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
        code.AppendLineAt(
            4,
            "var key = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(element);"
        );
        code.AppendLineAt(
            4,
            "if (__added is not null) foreach (var existing in __added) if ("
                + comparer
                + ".Equals("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
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
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
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
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
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
            code.AppendLineAt(3, "public void Update(" + elementType + " element)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));"
            );
            code.AppendLineAt(4, "EnsureGranular(\"Update\");");
            code.AppendLineAt(
                4,
                "var key = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(element);"
            );
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
        // Internal sparse setter for ChangeSet.ToPatch; installs an already-built element patch.
        if (hasPatch)
        {
            code.AppendLineAt(
                3,
                "internal void __SparseSetEdited("
                    + keyType
                    + " key, "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
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
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(4, "__edited[key] = patch;");
            code.AppendLineAt(3, "}");
        }
        SparseKeyedSequenceApplyEmitter.EmitKeyedApply(
            code,
            member,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            runtime
        );
        SparseKeyedSequenceApplyEmitter.EmitKeyedBetween(
            code,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            facade,
            runtime
        );
        SparseKeyedSequenceComposeEmitter.EmitKeyedCompose(
            code,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            runtime
        );
        code.AppendLineAt(
            3,
            "public "
                + patchName
                + " Invert("
                + optionalList
                + " baseline) => Between(Apply(baseline), baseline);"
        );
        SparseKeyedSequenceRebaseEmitter.EmitKeyedRebase(
            code,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            facade,
            dialect
        );
        SparsePatchStjEmitter.AppendKeyedStj(code, member, dialect);
        code.AppendLineAt(2, "}");
    }
}
