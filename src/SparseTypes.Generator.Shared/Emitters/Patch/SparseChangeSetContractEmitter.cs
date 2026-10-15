using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Read-only transition-contract surface for generated ChangeSets.</summary>
/// <remarks>Emits the product-configured change-set contract.</remarks>
internal static class SparseChangeSetContractEmitter
{
    /// <summary>Nullability contract for the <c>TryGet*</c> out parameters.</summary>
    private const string NotNullWhenTrue =
        "[global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] ";

    internal static bool HasContracts(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        dialect.TransitionContracts is not null;

    internal static string ChangeSetBases(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        if (!HasContracts(dialect))
            return string.Empty;

        var contracts = dialect.TransitionContracts!;
        if (
            modelType is not null
            && contracts.TypedChangeSetInterfaceType is string typedInterfaceType
        )
        {
            return " : " + typedInterfaceType + "<" + modelType + ">";
        }

        return " : " + contracts.ChangeSetInterfaceType;
    }

    internal static string ScalarBases(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string valueType
    ) =>
        HasContracts(dialect)
            ? " : " + dialect.TransitionContracts!.ValueChangeInterfaceType + "<" + valueType + ">"
            : string.Empty;

    internal static string KeyedBases(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseMemberModel member
    )
    {
        if (!HasContracts(dialect))
            return string.Empty;
        var contracts = dialect.TransitionContracts!;
        var open = SparseKeyedCollectionEmitter.HasTemporaryKey(member)
            ? contracts.TemporaryKeyedChangeInterfaceType
            : contracts.KeyedChangeInterfaceType;
        return ", "
            + open
            + "<"
            + KeyTypeOf(member)
            + ", "
            + ElementTypeOf(member)
            + ", "
            + ElementChangeSetOf(member)
            + ">";
    }

    internal static string KeyedItemBases(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseMemberModel member
    ) =>
        HasContracts(dialect)
            ? " : "
                + dialect.TransitionContracts!.KeyedItemChangeInterfaceType
                + "<"
                + KeyTypeOf(member)
                + ", "
                + ElementTypeOf(member)
                + ", "
                + ElementChangeSetOf(member)
                + ">"
            : string.Empty;

    internal static string DictionaryBases(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseMemberModel member
    )
    {
        if (!HasContracts(dialect))
            return string.Empty;
        var contracts = dialect.TransitionContracts!;
        return ", "
            + contracts.DictionaryChangeInterfaceType
            + "<"
            + KeyTypeOf(member)
            + ", "
            + ValueTypeOf(member)
            + ", "
            + DictEditedType(member)
            + ">";
    }

    internal static string DictionaryItemBases(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseMemberModel member
    )
    {
        if (!HasContracts(dialect))
            return string.Empty;
        var contracts = dialect.TransitionContracts!;
        if (member.Collection.ValueType?.IsFragmentModel == true)
            return " : "
                + contracts.StructuralDictionaryItemChangeInterfaceType
                + "<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ", "
                + ValueChangeSetOf(member)
                + ">";
        return " : "
            + contracts.DictionaryItemChangeInterfaceType
            + "<"
            + KeyTypeOf(member)
            + ", "
            + ValueTypeOf(member)
            + ">";
    }

    internal static bool IsStructuralDict(SparseMemberModel member) =>
        member.Collection.ValueType?.IsFragmentModel == true;

    internal static string DictEditedType(SparseMemberModel member) =>
        IsStructuralDict(member) ? ValueChangeSetOf(member) : ValueTypeOf(member);

    /// <summary>Appends explicit keyed lookup/enumeration contract members.</summary>
    /// <remarks>The public typed <c>GetChange</c> and <c>IEnumerable&lt.</remarks>
    internal static void AppendKeyedContractMembers(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasContracts(dialect))
            return;
        var contracts = dialect.TransitionContracts!;
        // GetChange lives on the base keyed contract even for temporary
        // members, while GetTemporaryChange lives on the derived temporary
        var keyed =
            contracts.KeyedChangeInterfaceType
            + "<"
            + KeyTypeOf(member)
            + ", "
            + ElementTypeOf(member)
            + ", "
            + ElementChangeSetOf(member)
            + ">";
        var item =
            contracts.KeyedItemChangeInterfaceType
            + "<"
            + KeyTypeOf(member)
            + ", "
            + ElementTypeOf(member)
            + ", "
            + ElementChangeSetOf(member)
            + ">";
        code.AppendLineAt(
            3,
            "/// <summary>Looks up the item change through the shared keyed contract.</summary>"
        );
        code.AppendLineAt(
            3,
            item + " " + keyed + ".GetChange(" + KeyTypeOf(member) + " key) => GetChange(key);"
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            var temporary =
                contracts.TemporaryKeyedChangeInterfaceType
                + "<"
                + KeyTypeOf(member)
                + ", "
                + ElementTypeOf(member)
                + ", "
                + ElementChangeSetOf(member)
                + ">";
            code.AppendLineAt(
                3,
                "/// <summary>Looks up the temporary change through the shared temporary contract.</summary>"
            );
            code.AppendLineAt(
                3,
                item
                    + " "
                    + temporary
                    + ".GetTemporaryChange(global::System.Guid temporaryKey) => GetTemporaryChange(temporaryKey);"
            );
        }
        code.AppendLineAt(
            3,
            "/// <summary>Enumerates item changes through the shared keyed contract.</summary>"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.IEnumerable<"
                + item
                + "> "
                + keyed
                + ".EnumerateItems() => global::System.Linq.Enumerable.Select(_items, static item => ("
                + item
                + ")item);"
        );
    }

    /// <summary>Appends explicit dictionary lookup/enumeration contract members.</summary>
    internal static void AppendDictionaryContractMembers(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasContracts(dialect))
            return;
        var contracts = dialect.TransitionContracts!;
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dict =
            contracts.DictionaryChangeInterfaceType
            + "<"
            + keyType
            + ", "
            + valueType
            + ", "
            + DictEditedType(member)
            + ">";
        var item =
            contracts.DictionaryItemChangeInterfaceType + "<" + keyType + ", " + valueType + ">";
        code.AppendLineAt(
            3,
            "/// <summary>Looks up the entry change through the shared dictionary contract.</summary>"
        );
        code.AppendLineAt(
            3,
            item + " " + dict + ".GetChange(" + keyType + " key) => GetChange(key);"
        );
        code.AppendLineAt(
            3,
            "/// <summary>Enumerates entry changes through the shared dictionary contract.</summary>"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.IEnumerable<"
                + item
                + "> "
                + dict
                + ".EnumerateEntries() => global::System.Linq.Enumerable.Select(_items, static item => ("
                + item
                + ")item);"
        );
    }

    /// <summary>Appends the ChangeSet-level contract: emptiness plus traversal entry points.</summary>
    /// <remarks>Everything projects the existing typed surface (properties.</remarks>
    internal static void AppendChangeSetContract(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasContracts(dialect))
            return;
        var contracts = dialect.TransitionContracts!;
        var iface = contracts.ChangeSetInterfaceType;
        ComputePublicNames(members, out var propNames, out _);
        code.AppendLineAt(
            2,
            "/// <summary>Gets whether this change set contains any semantic changes.</summary>"
        );
        code.AppendLineAt(2, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(
            2,
            "/// <summary>Enumerates flattened change records reusing the typed enumeration projection.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The flattened transitions.</returns>");
        code.AppendLineAt(
            2,
            "global::System.Collections.Generic.IReadOnlyList<"
                + contracts.ChangeRecordType
                + "> "
                + iface
                + ".EnumerateChangeRecords()"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var records = new global::System.Collections.Generic.List<"
                + contracts.ChangeRecordType
                + ">();"
        );
        code.AppendLineAt(3, "foreach (var change in EnumerateChanges())");
        code.AppendLineAt(
            4,
            "records.Add(new "
                + contracts.ChangeRecordType
                + "(change.Path, change.Before, change.After, ("
                + contracts.ChangeKindType
                + ")(int)change.Kind));"
        );
        code.AppendLineAt(3, "return records.AsReadOnly();");
        code.AppendLineAt(2, "}");
        AppendTryGetNested(code, members, propNames, iface);
        AppendTryGetValue(code, members, propNames, dialect, iface);
        AppendTryGetKeyed(code, members, propNames, dialect, iface);
        AppendTryGetDictionary(code, members, propNames, dialect, iface);
    }

    private static void AppendTryGetNested(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        string iface
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Gets the nested change set for a nested-model member.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"memberName\">The member CLR name.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"child\">The nested change set when the member is a nested model.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <returns>True when the member is a nested model; otherwise false.</returns>"
        );
        code.AppendLineAt(
            2,
            "bool "
                + iface
                + ".TryGetNestedChange(string memberName, "
                + NotNullWhenTrue
                + "out "
                + iface
                + "? child)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (memberName is null) { child = null; return false; }");
        foreach (var member in members)
        {
            if (!IsNested(member))
                continue;
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            code.AppendLineAt(
                3,
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal)) { child = "
                    + prop
                    + "; return true; }"
            );
        }
        code.AppendLineAt(3, "child = null;");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendTryGetValue(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string iface
    )
    {
        var contracts = dialect.TransitionContracts!;
        code.AppendLineAt(
            2,
            "/// <summary>Gets the scalar transition for an atomic member.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"memberName\">The member CLR name.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"change\">The transition on an exact member and value-type match.</param>"
        );
        code.AppendLineAt(2, "/// <returns>True on an exact match; otherwise false.</returns>");
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"T\">The expected member value type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "bool "
                + iface
                + ".TryGetValueChange<T>(string memberName, "
                + NotNullWhenTrue
                + "out "
                + contracts.ValueChangeInterfaceType
                + "<T>? change)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (memberName is null) { change = null; return false; }");
        foreach (var member in members)
        {
            if (IsNested(member) || IsKeyed(member) || IsDict(member))
                continue;
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            // Exact-match dispatch through interface pattern matching.
            code.AppendLineAt(
                3,
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal) && "
                    + prop
                    + " is "
                    + contracts.ValueChangeInterfaceType
                    + "<T> __sparse_match_"
                    + member.Id
                    + ") { change = __sparse_match_"
                    + member.Id
                    + "; return true; }"
            );
        }
        code.AppendLineAt(3, "change = null;");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendTryGetKeyed(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string iface
    )
    {
        var contracts = dialect.TransitionContracts!;
        code.AppendLineAt(
            2,
            "/// <summary>Gets the keyed-sequence transition for a keyed collection member.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"memberName\">The member CLR name.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"change\">The transition on an exact member and type match.</param>"
        );
        code.AppendLineAt(2, "/// <returns>True on an exact match; otherwise false.</returns>");
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TKey\">The expected stable key type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TElement\">The expected element type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TElementChange\">The expected element change-set type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "bool "
                + iface
                + ".TryGetKeyedChange<TKey, TElement, TElementChange>(string memberName, "
                + NotNullWhenTrue
                + "out "
                + contracts.KeyedChangeInterfaceType
                + "<TKey, TElement, TElementChange>? change)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (memberName is null) { change = null; return false; }");
        foreach (var member in members)
        {
            if (!IsKeyed(member))
                continue;
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            code.AppendLineAt(
                3,
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal) && "
                    + prop
                    + " is "
                    + contracts.KeyedChangeInterfaceType
                    + "<TKey, TElement, TElementChange> __sparse_match_"
                    + member.Id
                    + ") { change = __sparse_match_"
                    + member.Id
                    + "; return true; }"
            );
        }
        code.AppendLineAt(3, "change = null;");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendTryGetDictionary(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        System.Collections.Generic.Dictionary<int, string> propNames,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string iface
    )
    {
        var contracts = dialect.TransitionContracts!;
        code.AppendLineAt(
            2,
            "/// <summary>Gets the dictionary transition for a dictionary member.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"memberName\">The member CLR name.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"change\">The transition on an exact member and type match.</param>"
        );
        code.AppendLineAt(2, "/// <returns>True on an exact match; otherwise false.</returns>");
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TKey\">The expected dictionary key type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TValue\">The expected dictionary value type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "/// <typeparam name=\"TEdited\">The expected edited-projection type.</typeparam>"
        );
        code.AppendLineAt(
            2,
            "bool "
                + iface
                + ".TryGetDictionaryChange<TKey, TValue, TEdited>(string memberName, "
                + NotNullWhenTrue
                + "out "
                + contracts.DictionaryChangeInterfaceType
                + "<TKey, TValue, TEdited>? change)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (memberName is null) { change = null; return false; }");
        foreach (var member in members)
        {
            if (!IsDict(member))
                continue;
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            code.AppendLineAt(
                3,
                "if (global::System.String.Equals(memberName, "
                    + literal
                    + ", global::System.StringComparison.Ordinal) && "
                    + prop
                    + " is "
                    + contracts.DictionaryChangeInterfaceType
                    + "<TKey, TValue, TEdited> __sparse_match_"
                    + member.Id
                    + ") { change = __sparse_match_"
                    + member.Id
                    + "; return true; }"
            );
        }
        code.AppendLineAt(3, "change = null;");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
    }
}
